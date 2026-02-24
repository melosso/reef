using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Reef.Core.Models;
using Reef.Core.Parsers;
using Reef.Core.Sources;
using Reef.Core.Targets;
using Serilog;
using Serilog.Context;

namespace Reef.Core.Services;

/// <summary>
/// Executes an import profile through all pipeline phases:
///   1. Load profile + target connection
///   2. Pre-process (optional SQL/script)
///   3. Fetch source files (with retry)
///   4. Parse rows (streaming)
///   5. Column mapping + value transformation
///   6. Delta sync filtering (optional)
///   7. Write batches to target DB (Insert/Upsert/FullReplace/Append)
///   8. Commit delta sync state (optional)
///   9. Archive source files (optional)
///  10. Apply deletes (delta sync, optional)
///  11. Post-process (optional SQL/script)
///  12. Finalise execution record + notify
/// </summary>
public class ImportExecutionService
{
    private readonly ImportProfileService _profileService;
    private readonly ConnectionService _connectionService;
    private readonly DatabaseConfig _reefDbConfig;
    private readonly DatabaseImportTarget _databaseImportTarget;
    private readonly LocalFileImportTarget _localFileImportTarget;
    private readonly NotificationService _notificationService;
    private static readonly Serilog.ILogger Log = Serilog.Log.ForContext<ImportExecutionService>();

    public ImportExecutionService(
        ImportProfileService profileService,
        ConnectionService connectionService,
        DatabaseConfig reefDbConfig,
        DatabaseImportTarget databaseImportTarget,
        LocalFileImportTarget localFileImportTarget,
        NotificationService notificationService)
    {
        _profileService = profileService;
        _connectionService = connectionService;
        _reefDbConfig = reefDbConfig;
        _databaseImportTarget = databaseImportTarget;
        _localFileImportTarget = localFileImportTarget;
        _notificationService = notificationService;
    }

    // ── Public Entry Points ────────────────────────────────────────────

    public async Task<ImportProfileExecution> ExecuteAsync(
        int importProfileId,
        string triggeredBy = "Manual",
        CancellationToken ct = default)
    {
        var profile = await _profileService.GetByIdAsync(importProfileId)
            ?? throw new InvalidOperationException($"ImportProfile {importProfileId} not found");

        var exec = new ImportProfileExecution
        {
            ImportProfileId = importProfileId,
            Status = "Running",
            TriggeredBy = triggeredBy,
            StartedAt = DateTime.UtcNow,
            CurrentPhase = "Initialising"
        };

        exec.Id = await _profileService.CreateExecutionAsync(exec);
        var phaseTimings = new Dictionary<string, long>();

        // Push profile code into log context so all downstream messages carry it
        using var profileLogCtx = LogContext.PushProperty("ProfileCode", $"[{profile.Code}] ");

        Log.Information("ImportExecution {Id} started for profile {ProfileCode} ({ProfileName})",
            exec.Id, profile.Code, profile.Name);

        try
        {
            await RunPipelineAsync(profile, exec, phaseTimings, ct);

            exec.Status = exec.RowsFailed > 0 ? "PartialSuccess" : "Success";
        }
        catch (OperationCanceledException)
        {
            exec.Status = "Failed";
            exec.ErrorMessage = "Execution was cancelled";
            Log.Warning("ImportExecution {Id} cancelled", exec.Id);
        }
        catch (Exception ex)
        {
            exec.Status = "Failed";
            exec.ErrorMessage = ex.Message;
            exec.StackTrace = ex.StackTrace;
            Log.Error("ImportExecution {Id} failed in phase '{Phase}': {Error}", exec.Id, exec.CurrentPhase, ex.Message);
        }
        finally
        {
            exec.CompletedAt = DateTime.UtcNow;
            exec.CurrentPhase = null;
            exec.PhaseTimingsJson = JsonSerializer.Serialize(phaseTimings);
            try
            {
                await _profileService.UpdateExecutionAsync(exec);
            }
            catch (Exception ex)
            {
                Log.Error("ImportExecution {Id}: failed to update execution record: {Error}", exec.Id, ex.Message);
            }
            try
            {
                await _profileService.UpdateLastExecutedAsync(importProfileId);
            }
            catch (Exception ex)
            {
                Log.Error("ImportExecution {Id}: failed to update LastExecutedAt on profile {ProfileCode}: {Error}", exec.Id, profile.Code, ex.Message);
            }

            Log.Information(
                "ImportExecution {Id} finished: {Status}. " +
                "Read={Read} Inserted={Ins} Updated={Upd} Skipped={Skip} Failed={Fail} Deleted={Del}",
                exec.Id, exec.Status,
                exec.TotalRowsRead, exec.RowsInserted, exec.RowsUpdated,
                exec.RowsSkipped, exec.RowsFailed, exec.RowsDeleted);

            try
            {
                if (exec.Status == "Failed")
                    _ = _notificationService.SendImportExecutionFailureAsync(exec, profile);
                else
                    _ = _notificationService.SendImportExecutionSuccessAsync(exec, profile);
            }
            catch (Exception ex)
            {
                Log.Warning("ImportExecution {Id}: failed to send notification: {Error}", exec.Id, ex.Message);
            }
        }

        return exec;
    }

    // ── Pipeline ───────────────────────────────────────────────────────

    private async Task RunPipelineAsync(
        ImportProfileWithNames profile,
        ImportProfileExecution exec,
        Dictionary<string, long> phaseTimings,
        CancellationToken ct)
    {
        // ── Phase 1: Load target connection (Database target only) ───
        bool isLocalFile = profile.TargetType?.Equals("LocalFile", StringComparison.OrdinalIgnoreCase) == true;
        Connection? connection = null;
        string? decryptedConnStr = null;

        if (!isLocalFile)
        {
            connection = await _connectionService.GetByIdAsync(profile.TargetConnectionId ?? 0)
                ?? throw new InvalidOperationException(
                    $"Target connection {profile.TargetConnectionId} not found");
            decryptedConnStr = connection.ConnectionString;
        }

        // ── Phase 2: Pre-process (Database target only) ─────────────
        if (!isLocalFile && !string.IsNullOrWhiteSpace(profile.PreProcessType))
        {
            await TimedPhaseAsync("PreProcess", exec, phaseTimings, ct, async () =>
            {
                await RunSqlProcessAsync(
                    connection!.Type, decryptedConnStr!,
                    profile.PreProcessConfig, exec.Id, ct);
            });
        }

        // ── Phase 3: Fetch source files ─────────────────────────────
        List<ImportSourceFile> sourceFiles = null!;
        await TimedPhaseAsync("FetchSource", exec, phaseTimings, ct, async () =>
        {
            sourceFiles = await FetchWithRetryAsync(profile, exec, ct);
        });

        // ── Phase 4 + 5 + 6 + 7: Parse → Map → Delta → Write ────────
        // Parse is streaming; batches are collected then written
        var formatConfig = ParseFormatConfig(profile.FormatConfig);
        var columnMappings = ParseColumnMappings(profile.ColumnMappingsJson);
        var upsertKeys = (profile.UpsertKeyColumns ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        // Also collect per-column key flags from mappings
        upsertKeys.AddRange(columnMappings
            .Where(m => m.IsKeyColumn && !upsertKeys.Contains(m.TargetColumn, StringComparer.OrdinalIgnoreCase))
            .Select(m => m.TargetColumn));

        var writeContext = new ImportWriteContext
        {
            TargetType = profile.TargetType ?? "Database",
            TargetConnection = connection,
            TargetTable = profile.TargetTable ?? "",
            LoadStrategy = profile.LoadStrategy,
            UpsertKeyColumns = upsertKeys,
            ColumnMappings = columnMappings,
            CommandTimeoutSeconds = profile.CommandTimeoutSeconds,
            OnRowFailure = profile.OnRowFailure,
            OnConstraintViolation = profile.OnConstraintViolation,
            BatchSize = profile.BatchSize,
            TargetFilePath = isLocalFile && !string.IsNullOrWhiteSpace(profile.LocalTargetPath)
                ? ExpandPathTemplate(profile.LocalTargetPath, profile)
                : profile.LocalTargetPath,
            TargetFormat = profile.LocalTargetFormat ?? "CSV",
            TargetWriteMode = profile.LocalTargetWriteMode ?? "Overwrite"
        };

        IImportTarget target = isLocalFile ? _localFileImportTarget : _databaseImportTarget;
        List<TargetColumnInfo> tableSchema = new();

        if (!isLocalFile)
        {
            await TimedPhaseAsync("GetSchema", exec, phaseTimings, ct, async () =>
            {
                try { tableSchema = await _databaseImportTarget.GetTableSchemaAsync(connection!, profile.TargetTable!, ct); }
                catch (Exception ex) { Log.Warning("Could not retrieve schema for {Table}: {Error}", profile.TargetTable, ex.Message); }
            });
        }

        // Load delta sync state if enabled
        Dictionary<string, string> previousHashes = new();
        if (profile.DeltaSyncEnabled && !string.IsNullOrWhiteSpace(profile.DeltaSyncReefIdColumn))
        {
            await TimedPhaseAsync("LoadDeltaState", exec, phaseTimings, ct, async () =>
            {
                previousHashes = await LoadDeltaHashesAsync(profile.Id);
            });
        }

        // Collect all rows for FullReplace; stream batches for others
        var currentHashState = new Dictionary<string, string>();
        var batch = new List<Dictionary<string, object?>>();

        bool isFullReplace = profile.LoadStrategy.Equals("FullReplace", StringComparison.OrdinalIgnoreCase);
        var allRowsForFullReplace = isFullReplace ? new List<Dictionary<string, object?>>() : null;

        await TimedPhaseAsync("ParseAndWrite", exec, phaseTimings, ct, async () =>
        {
            foreach (var file in sourceFiles)
            {
                ct.ThrowIfCancellationRequested();
                exec.FilesProcessed++;

                var parser = ImportParserFactory.Create(profile.SourceFormat);

                await foreach (var row in parser.ParseAsync(file.Content, formatConfig, ct))
                {
                    exec.TotalRowsRead++;
                    exec.BytesProcessed += EstimateRowBytes(row.Columns);

                    if (!string.IsNullOrWhiteSpace(row.ParseError))
                    {
                        await HandleParseFailure(profile, exec, row, ct);
                        if (exec.Status == "Aborted") return;
                        continue;
                    }

                    if (row.IsSkipped) { exec.RowsSkipped++; continue; }

                    // Apply column mapping
                    var mapped = ApplyColumnMapping(row.Columns, columnMappings, profile, tableSchema);

                    if (mapped is null) { exec.RowsSkipped++; continue; }

                    // Delta sync filtering
                    if (profile.DeltaSyncEnabled && !string.IsNullOrWhiteSpace(profile.DeltaSyncReefIdColumn))
                    {
                        var reefIdKey = profile.DeltaSyncReefIdColumn!;
                        if (mapped.TryGetValue(reefIdKey, out var reefIdVal) && reefIdVal is not null)
                        {
                            var reefId = reefIdVal.ToString()!.Trim();
                            var hash = ComputeRowHash(mapped, profile.DeltaSyncHashAlgorithm ?? "SHA256");
                            currentHashState[reefId] = hash;

                            if (previousHashes.TryGetValue(reefId, out var prevHash) && prevHash == hash)
                            {
                                exec.DeltaSyncUnchangedRows++;
                                exec.RowsSkipped++;
                                continue;
                            }

                            if (previousHashes.ContainsKey(reefId))
                                exec.DeltaSyncChangedRows++;
                            else
                                exec.DeltaSyncNewRows++;
                        }
                        // If ReefId column missing or null: write the row without delta sync tracking
                    }

                    if (isFullReplace)
                    {
                        allRowsForFullReplace!.Add(mapped);
                    }
                    else
                    {
                        batch.Add(mapped);
                        if (batch.Count >= profile.BatchSize)
                        {
                            await WriteBatchAsync(target, batch, writeContext, exec, profile, ct);
                            batch.Clear();
                        }
                    }

                    // Check abort thresholds
                    if (ShouldAbort(profile, exec))
                    {
                        exec.Status = "Aborted";
                        exec.ErrorMessage = "Exceeded max failed row threshold";
                        return;
                    }
                }
            }

            // Flush remaining non-FullReplace batch
            if (batch.Count > 0)
                await WriteBatchAsync(target, batch, writeContext, exec, profile, ct);

            // FullReplace: truncate then insert all
            if (isFullReplace && allRowsForFullReplace!.Count > 0)
            {
                await TimedPhaseAsync("FullReplace", exec, phaseTimings, ct, async () =>
                {
                    var result = await target.FullReplaceAsync(allRowsForFullReplace, writeContext, ct);
                    exec.RowsInserted += result.RowsInserted;
                    exec.RowsFailed += result.RowsFailed;
                    await PersistRowErrors(result.Errors, exec.Id, "FullReplace");
                });
            }
        });

        if (exec.Status == "Aborted")
        {
            exec.Status = "Failed";
            return;
        }

        // ── Phase 8: Commit delta sync state ─────────────────────────
        if (profile.DeltaSyncEnabled && currentHashState.Any())
        {
            await TimedPhaseAsync("CommitDeltaState", exec, phaseTimings, ct, async () =>
            {
                await CommitDeltaHashesAsync(profile.Id, exec.Id, currentHashState, previousHashes);
            });
        }

        // Warn when Smart Sync was enabled but no rows were tracked (ReefId column missing in data)
        if (profile.DeltaSyncEnabled
            && !string.IsNullOrWhiteSpace(profile.DeltaSyncReefIdColumn)
            && exec.TotalRowsRead > 0
            && currentHashState.Count == 0)
        {
            await _profileService.AddExecutionErrorAsync(new ImportExecutionError
            {
                ExecutionId  = exec.Id,
                ErrorType    = "Configuration",
                Phase        = "DeltaSync",
                ErrorMessage =
                    $"Smart Sync is enabled but column '{profile.DeltaSyncReefIdColumn}' " +
                    $"was not found in any of the {exec.TotalRowsRead} row(s) processed. " +
                    "Check the ReefId Column setting in Smart Sync.",
                OccurredAt   = DateTime.UtcNow
            });
        }

        // ── Phase 9: Archive source files ─────────────────────────────
        if (profile.ArchiveAfterImport)
        {
            await TimedPhaseAsync("Archive", exec, phaseTimings, ct, async () =>
            {
                var source = ImportSourceFactory.Create(profile.SourceType);
                foreach (var file in sourceFiles)
                {
                    try
                    {
                        await source.ArchiveAsync(profile, file.Identifier, ct);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning("ImportExecution {Id}: archive failed for {File}: {Error}", exec.Id, file.Identifier, ex.Message);
                        try
                        {
                            await _profileService.AddExecutionErrorAsync(new ImportExecutionError
                            {
                                ExecutionId  = exec.Id,
                                ErrorType    = "Archive",
                                Phase        = "Archive",
                                ErrorMessage = $"Archive failed for '{file.Identifier}': {ex.Message}",
                                OccurredAt   = DateTime.UtcNow
                            });
                        }
                        catch { /* never let error-recording abort execution */ }
                    }
                }
            });
        }

        // ── Phase 10: Apply deletes (delta sync) ──────────────────────
        if (profile.DeltaSyncEnabled && profile.DeltaSyncTrackDeletes && previousHashes.Any())
        {
            var deletedIds = previousHashes.Keys
                .Except(currentHashState.Keys)
                .ToList();

            if (deletedIds.Any())
            {
                await TimedPhaseAsync("ApplyDeletes", exec, phaseTimings, ct, async () =>
                {
                    var deleted = await target.ApplyDeletesAsync(
                        deletedIds, profile.DeltaSyncReefIdColumn!, profile, ct);
                    exec.RowsDeleted += deleted;
                    exec.DeltaSyncDeletedRows += deleted;
                    await MarkDeltaDeletedAsync(profile.Id, exec.Id, deletedIds);
                });
            }
        }

        // ── Phase 11: Post-process (Database target only) ─────────────
        if (!isLocalFile && !string.IsNullOrWhiteSpace(profile.PostProcessType))
        {
            await TimedPhaseAsync("PostProcess", exec, phaseTimings, ct, async () =>
            {
                try
                {
                    await RunSqlProcessAsync(
                        connection!.Type, decryptedConnStr!,
                        profile.PostProcessConfig, exec.Id, ct);
                }
                catch (Exception ex)
                {
                    Log.Warning("ImportExecution {Id}: post-process failed: {Error}", exec.Id, ex.Message);
                    if (!profile.PostProcessSkipOnFailure)
                        throw;
                }
            });
        }
    }

    // ── Source Fetch with Retry ────────────────────────────────────────

    private async Task<List<ImportSourceFile>> FetchWithRetryAsync(
        ImportProfile profile,
        ImportProfileExecution exec,
        CancellationToken ct)
    {
        var source = ImportSourceFactory.Create(profile.SourceType);
        var maxAttempts = Math.Max(1, profile.RetryCount);
        Exception? lastEx = null;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var files = await source.FetchAsync(profile, ct);
                return files;
            }
            catch (Exception ex)
            {
                lastEx = ex;
                Log.Warning("ImportExecution {Id}: source fetch attempt {Attempt}/{Max} failed: {Error}",
                    exec.Id, attempt, maxAttempts, ex.Message);

                if (attempt < maxAttempts)
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)), ct);
            }
        }

        // All attempts failed — apply OnSourceFailure strategy
        var errMsg = $"Source fetch failed after {maxAttempts} attempt(s): {lastEx?.Message}";
        await _profileService.AddExecutionErrorAsync(new ImportExecutionError
        {
            ExecutionId = exec.Id,
            ErrorMessage = errMsg,
            Phase = "FetchSource",
            ErrorType = "Source",
            OccurredAt = DateTime.UtcNow
        });

        if (profile.OnSourceFailure.Equals("Skip", StringComparison.OrdinalIgnoreCase))
        {
            Log.Warning("ImportExecution {Id}: source failure skipped per OnSourceFailure=Skip", exec.Id);
            return new List<ImportSourceFile>();
        }

        throw new InvalidOperationException(errMsg, lastEx);
    }

    // ── Batch Write ────────────────────────────────────────────────────

    private async Task WriteBatchAsync(
        IImportTarget target,
        List<Dictionary<string, object?>> batch,
        ImportWriteContext ctx,
        ImportProfileExecution exec,
        ImportProfile profile,
        CancellationToken ct)
    {
        try
        {
            var result = await target.WriteBatchAsync(batch, ctx, ct);
            exec.RowsInserted += result.RowsInserted;
            exec.RowsUpdated += result.RowsUpdated;
            exec.RowsSkipped += result.RowsSkipped;
            exec.RowsFailed += result.RowsFailed;
            await PersistRowErrors(result.Errors, exec.Id, "Write");
        }
        catch (Exception ex)
        {
            Log.Error("ImportExecution {Id}: batch write failed: {Error}", exec.Id, ex.Message);
            await _profileService.AddExecutionErrorAsync(new ImportExecutionError
            {
                ExecutionId = exec.Id,
                ErrorMessage = ex.Message,
                Phase = "Write",
                ErrorType = "Batch",
                OccurredAt = DateTime.UtcNow
            });

            if (profile.OnRowFailure.Equals("Fail", StringComparison.OrdinalIgnoreCase))
                throw;

            exec.RowsFailed += batch.Count;
        }
    }

    // ── Parse Failure Handling ─────────────────────────────────────────

    private async Task HandleParseFailure(
        ImportProfile profile,
        ImportProfileExecution exec,
        ParsedRow row,
        CancellationToken ct)
    {
        exec.RowsFailed++;
        await _profileService.AddExecutionErrorAsync(new ImportExecutionError
        {
            ExecutionId = exec.Id,
            RowNumber = row.LineNumber,
            ErrorMessage = row.ParseError ?? "Parse error",
            Phase = "Parse",
            ErrorType = "Parse",
            RowDataJson = row.Columns.Any()
                ? JsonSerializer.Serialize(row.Columns)
                : null,
            OccurredAt = DateTime.UtcNow
        });

        if (profile.OnParseFailure.Equals("Fail", StringComparison.OrdinalIgnoreCase))
        {
            exec.Status = "Aborted";
        }
    }

    // ── Column Mapping ─────────────────────────────────────────────────

    private static Dictionary<string, object?>? ApplyColumnMapping(
        Dictionary<string, object?> sourceColumns,
        List<ImportColumnMapping> mappings,
        ImportProfile profile,
        List<TargetColumnInfo> schema)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        if (mappings.Any())
        {
            foreach (var m in mappings)
            {
                object? value = null;

                if (sourceColumns.TryGetValue(m.SourceColumn, out var raw))
                    value = raw;
                else if (!string.IsNullOrWhiteSpace(m.DefaultValue))
                    value = m.DefaultValue;

                if (value is null && m.SkipOnNull)
                    continue;

                if (value is null && !string.IsNullOrWhiteSpace(m.DefaultValue))
                    value = m.DefaultValue;

                result[m.TargetColumn] = CastValue(value, m.DataType);
            }
        }
        else if (profile.AutoMapColumns)
        {
            // Auto-map by case-insensitive name match
            var schemaNames = schema.Select(s => s.ColumnName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var (col, val) in sourceColumns)
            {
                if (schemaNames.Contains(col) || !profile.SkipUnmappedColumns)
                    result[col] = val;
            }

            if (!result.Any() && sourceColumns.Any())
            {
                // No schema available — pass through all columns
                foreach (var (col, val) in sourceColumns)
                    result[col] = val;
            }
        }
        else
        {
            // No mappings, no auto-map — pass through everything
            foreach (var (col, val) in sourceColumns)
                result[col] = val;
        }

        return result.Any() ? result : null;
    }

    private static object? CastValue(object? value, string? dataType)
    {
        if (value is null) return null;
        var str = value.ToString()!;

        return dataType?.ToLowerInvariant() switch
        {
            "int" or "integer" or "long" =>
                long.TryParse(str, out var l) ? l : (object?)null,
            "decimal" or "float" or "double" or "number" =>
                decimal.TryParse(str, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : (object?)null,
            "bool" or "boolean" =>
                str is "1" or "true" or "yes" or "True" or "TRUE" or "YES" ? true :
                str is "0" or "false" or "no" or "False" or "FALSE" or "NO" ? false : (object?)null,
            "datetime" or "date" =>
                DateTime.TryParse(str, null, System.Globalization.DateTimeStyles.RoundtripKind,
                    out var dt) ? dt : (object?)null,
            _ => value
        };
    }

    // ── Delta Sync Helpers ─────────────────────────────────────────────

    private async Task<Dictionary<string, string>> LoadDeltaHashesAsync(int importProfileId)
    {
        await using var conn = new SqliteConnection(_reefDbConfig.ConnectionString);
        await conn.OpenAsync();

        var rows = await conn.QueryAsync<(string ReefId, string RowHash)>(
            @"SELECT ReefId, RowHash FROM ImportDeltaSyncState
              WHERE ImportProfileId = @ProfileId AND IsDeleted = 0",
            new { ProfileId = importProfileId });

        return rows.ToDictionary(r => r.ReefId, r => r.RowHash, StringComparer.Ordinal);
    }

    private async Task CommitDeltaHashesAsync(
        int importProfileId, int executionId,
        Dictionary<string, string> currentState,
        Dictionary<string, string> previousState)
    {
        await using var conn = new SqliteConnection(_reefDbConfig.ConnectionString);
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        foreach (var (reefId, hash) in currentState)
        {
            if (previousState.ContainsKey(reefId))
            {
                await conn.ExecuteAsync(
                    @"UPDATE ImportDeltaSyncState
                      SET RowHash = @Hash, LastSeenExecutionId = @ExecId,
                          LastSeenAt = datetime('now'), IsDeleted = 0, DeletedAt = NULL
                      WHERE ImportProfileId = @ProfileId AND ReefId = @ReefId",
                    new { Hash = hash, ExecId = executionId, ProfileId = importProfileId, ReefId = reefId },
                    tx);
            }
            else
            {
                await conn.ExecuteAsync(
                    @"INSERT OR IGNORE INTO ImportDeltaSyncState
                          (ImportProfileId, ReefId, RowHash, LastSeenExecutionId)
                      VALUES (@ProfileId, @ReefId, @Hash, @ExecId)",
                    new { ProfileId = importProfileId, ReefId = reefId, Hash = hash, ExecId = executionId },
                    tx);
            }
        }

        await tx.CommitAsync();
    }

    private async Task MarkDeltaDeletedAsync(
        int importProfileId, int executionId, List<string> deletedIds)
    {
        await using var conn = new SqliteConnection(_reefDbConfig.ConnectionString);
        await conn.OpenAsync();

        foreach (var reefId in deletedIds)
        {
            await conn.ExecuteAsync(
                @"UPDATE ImportDeltaSyncState
                  SET IsDeleted = 1, DeletedAt = datetime('now'),
                      LastSeenExecutionId = @ExecId
                  WHERE ImportProfileId = @ProfileId AND ReefId = @ReefId",
                new { ExecId = executionId, ProfileId = importProfileId, ReefId = reefId });
        }
    }

    // ── Pre/Post SQL Processing ────────────────────────────────────────

    private static async Task RunSqlProcessAsync(
        string dbType, string connectionString,
        string? processConfig, int execId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(processConfig)) return;

        string? sql = null;
        try
        {
            using var doc = JsonDocument.Parse(processConfig);
            doc.RootElement.TryGetProperty("sql", out var sqlEl);
            doc.RootElement.TryGetProperty("Sql", out sqlEl);
            sql = sqlEl.GetString();
        }
        catch
        {
            sql = processConfig; // treat as raw SQL
        }

        if (string.IsNullOrWhiteSpace(sql)) return;

        // Replace placeholder tokens
        sql = sql.Replace("{ExecutionId}", execId.ToString());

        await using var dbConn = CreateDbConnection(dbType, connectionString);
        await dbConn.OpenAsync(ct);
        await dbConn.ExecuteAsync(new CommandDefinition(sql, cancellationToken: ct));
    }

    // ── Utility ────────────────────────────────────────────────────────

    private static async Task TimedPhaseAsync(
        string phase,
        ImportProfileExecution exec,
        Dictionary<string, long> timings,
        CancellationToken ct,
        Func<Task> work)
    {
        ct.ThrowIfCancellationRequested();
        exec.CurrentPhase = phase;
        var sw = Stopwatch.StartNew();
        try
        {
            await work();
        }
        finally
        {
            sw.Stop();
            timings[phase] = sw.ElapsedMilliseconds;
        }
    }

    private static string ComputeRowHash(
        Dictionary<string, object?> row, string algorithm)
    {
        var sb = new StringBuilder();
        foreach (var k in row.Keys.OrderBy(k => k))
            sb.Append($"{k}={row[k]};");

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var hash = algorithm.ToUpperInvariant() == "MD5"
            ? MD5.HashData(bytes)
            : SHA256.HashData(bytes);

        return Convert.ToHexString(hash);
    }

    private static bool ShouldAbort(ImportProfile profile, ImportProfileExecution exec)
    {
        if (profile.MaxFailedRowsBeforeAbort > 0 && exec.RowsFailed >= profile.MaxFailedRowsBeforeAbort)
            return true;

        if (profile.MaxFailedRowsPercent > 0 && exec.TotalRowsRead > 0)
        {
            var pct = exec.RowsFailed * 100.0 / exec.TotalRowsRead;
            if (pct >= profile.MaxFailedRowsPercent)
                return true;
        }

        return false;
    }

    private static long EstimateRowBytes(Dictionary<string, object?> row)
        => row.Values.Sum(v => v?.ToString()?.Length ?? 0) * 2L; // approx UTF-16

    private async Task PersistRowErrors(List<ImportRowError> errors, int execId, string phase)
    {
        foreach (var e in errors.Take(100)) // cap at 100 stored errors per batch
        {
            await _profileService.AddExecutionErrorAsync(new ImportExecutionError
            {
                ExecutionId = execId,
                RowNumber = e.RowNumber,
                ReefId = e.ReefId,
                ErrorType = e.ErrorType,
                ErrorMessage = e.ErrorMessage,
                RowDataJson = e.RowData is not null
                    ? JsonSerializer.Serialize(e.RowData)
                    : null,
                Phase = phase,
                OccurredAt = DateTime.UtcNow
            });
        }
    }

    // ── Format / Mapping Deserialization ──────────────────────────────

    private static ImportFormatConfig ParseFormatConfig(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new ImportFormatConfig();
        try { return JsonSerializer.Deserialize<ImportFormatConfig>(json) ?? new ImportFormatConfig(); }
        catch { return new ImportFormatConfig(); }
    }

    private static List<ImportColumnMapping> ParseColumnMappings(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<ImportColumnMapping>();
        try { return JsonSerializer.Deserialize<List<ImportColumnMapping>>(json) ?? new List<ImportColumnMapping>(); }
        catch { return new List<ImportColumnMapping>(); }
    }

    private static System.Data.Common.DbConnection CreateDbConnection(string dbType, string connectionString)
    {
        return dbType.ToUpperInvariant() switch
        {
            "SQLSERVER" or "SQL SERVER" or "MSSQL" =>
                new Microsoft.Data.SqlClient.SqlConnection(connectionString),
            "MYSQL" or "MARIADB" =>
                new MySqlConnector.MySqlConnection(connectionString),
            "POSTGRESQL" or "POSTGRES" or "PGSQL" =>
                new Npgsql.NpgsqlConnection(connectionString),
            _ => throw new NotSupportedException($"Unsupported DB type: {dbType}")
        };
    }

    private static string ExpandPathTemplate(string template, ImportProfileWithNames profile)
    {
        var now = DateTime.UtcNow;
        var safeName = string.Concat(profile.Name.Select(c =>
            Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return template
            .Replace("{profile}", safeName)
            .Replace("{timestamp}", now.ToString("yyyyMMdd_HHmmss"))
            .Replace("{date}", now.ToString("yyyyMMdd"))
            .Replace("{time}", now.ToString("HHmmss"))
            .Replace("{guid}", Guid.NewGuid().ToString("N"))
            .Replace("{format}", (profile.LocalTargetFormat ?? "csv").ToLowerInvariant());
    }
}
