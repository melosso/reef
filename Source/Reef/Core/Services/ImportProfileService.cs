using Dapper;
using Microsoft.Data.Sqlite;
using Reef.Core.Models;
using Reef.Core.Security;
using Serilog;

namespace Reef.Core.Services;

/// <summary>
/// Manages Import Profile CRUD operations.
/// Mirrors the ProfileService pattern but for ImportProfiles.
/// </summary>
public class ImportProfileService
{
    private readonly string _connectionString;
    private readonly EncryptionService _encryptionService;
    private readonly HashValidator _hashValidator;
    private static readonly Serilog.ILogger Log = Serilog.Log.ForContext<ImportProfileService>();

    public ImportProfileService(
        DatabaseConfig config,
        EncryptionService encryptionService,
        HashValidator hashValidator)
    {
        _connectionString = config.ConnectionString;
        _encryptionService = encryptionService;
        _hashValidator = hashValidator;
    }

    // ── Queries ────────────────────────────────────────────────────────

    public async Task<List<ImportProfileWithNames>> GetAllAsync()
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        const string sql = @"
            SELECT
                ip.*,
                c.Name  AS TargetConnectionName,
                c.Type  AS TargetConnectionType,
                pg.Name AS GroupName
            FROM ImportProfiles ip
            LEFT JOIN  Connections c  ON ip.TargetConnectionId = c.Id
            LEFT JOIN  ProfileGroups pg ON ip.GroupId = pg.Id
            ORDER BY ip.Name";

        return (await conn.QueryAsync<ImportProfileWithNames>(sql)).ToList();
    }

    public async Task<ImportProfileWithNames?> GetByIdAsync(int id)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        const string sql = @"
            SELECT
                ip.*,
                c.Name  AS TargetConnectionName,
                c.Type  AS TargetConnectionType,
                pg.Name AS GroupName
            FROM ImportProfiles ip
            LEFT JOIN  Connections c  ON ip.TargetConnectionId = c.Id
            LEFT JOIN  ProfileGroups pg ON ip.GroupId = pg.Id
            WHERE ip.Id = @Id";

        return await conn.QueryFirstOrDefaultAsync<ImportProfileWithNames>(sql, new { Id = id });
    }

    public async Task<List<ImportProfileWithNames>> GetByGroupIdAsync(int groupId)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        const string sql = @"
            SELECT ip.*, c.Name AS TargetConnectionName, c.Type AS TargetConnectionType, pg.Name AS GroupName
            FROM ImportProfiles ip
            LEFT JOIN Connections c ON ip.TargetConnectionId = c.Id
            LEFT JOIN ProfileGroups pg ON ip.GroupId = pg.Id
            WHERE ip.GroupId = @GroupId ORDER BY ip.Name";

        return (await conn.QueryAsync<ImportProfileWithNames>(sql, new { GroupId = groupId })).ToList();
    }

    // ── Create ─────────────────────────────────────────────────────────

    public async Task<int> CreateAsync(ImportProfile profile, int? createdByUserId)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        try
        {
            await ValidateAsync(conn, profile);

            profile.Hash = _hashValidator.ComputeHash(profile);
            profile.CreatedBy = createdByUserId;
            profile.CreatedAt = DateTime.UtcNow;
            profile.UpdatedAt = DateTime.UtcNow;

            const string sql = @"
                INSERT INTO ImportProfiles (
                    Name, GroupId,
                    SourceType, SourceDestinationId, SourceConfig,
                    SourceFilePath, SourceFilePattern, SourceFileSelection,
                    ArchiveAfterImport, ArchivePath,
                    HttpMethod, HttpBodyTemplate, HttpPaginationEnabled, HttpPaginationConfig, HttpDataRootPath,
                    SourceFormat, FormatConfig,
                    ColumnMappingsJson, AutoMapColumns, SkipUnmappedColumns,
                    TargetType, TargetConnectionId, TargetTable, LoadStrategy, UpsertKeyColumns,
                    LocalTargetPath, LocalTargetFormat, LocalTargetWriteMode,
                    BatchSize, CommandTimeoutSeconds,
                    DeltaSyncEnabled, DeltaSyncReefIdColumn, DeltaSyncHashAlgorithm,
                    DeltaSyncTrackDeletes, DeltaSyncDeleteStrategy, DeltaSyncDeleteColumn, DeltaSyncDeleteValue,
                    DeltaSyncRetentionDays,
                    OnSourceFailure, OnParseFailure, OnRowFailure, OnConstraintViolation,
                    MaxFailedRowsBeforeAbort, MaxFailedRowsPercent, RollbackOnAbort, RetryCount,
                    PreProcessType, PreProcessConfig, PreProcessRollbackOnFailure,
                    PostProcessType, PostProcessConfig, PostProcessSkipOnFailure,
                    NotificationConfig, IsEnabled, Hash, CreatedAt, UpdatedAt, CreatedBy
                ) VALUES (
                    @Name, @GroupId,
                    @SourceType, @SourceDestinationId, @SourceConfig,
                    @SourceFilePath, @SourceFilePattern, @SourceFileSelection,
                    @ArchiveAfterImport, @ArchivePath,
                    @HttpMethod, @HttpBodyTemplate, @HttpPaginationEnabled, @HttpPaginationConfig, @HttpDataRootPath,
                    @SourceFormat, @FormatConfig,
                    @ColumnMappingsJson, @AutoMapColumns, @SkipUnmappedColumns,
                    @TargetType, @TargetConnectionId, @TargetTable, @LoadStrategy, @UpsertKeyColumns,
                    @LocalTargetPath, @LocalTargetFormat, @LocalTargetWriteMode,
                    @BatchSize, @CommandTimeoutSeconds,
                    @DeltaSyncEnabled, @DeltaSyncReefIdColumn, @DeltaSyncHashAlgorithm,
                    @DeltaSyncTrackDeletes, @DeltaSyncDeleteStrategy, @DeltaSyncDeleteColumn, @DeltaSyncDeleteValue,
                    @DeltaSyncRetentionDays,
                    @OnSourceFailure, @OnParseFailure, @OnRowFailure, @OnConstraintViolation,
                    @MaxFailedRowsBeforeAbort, @MaxFailedRowsPercent, @RollbackOnAbort, @RetryCount,
                    @PreProcessType, @PreProcessConfig, @PreProcessRollbackOnFailure,
                    @PostProcessType, @PostProcessConfig, @PostProcessSkipOnFailure,
                    @NotificationConfig, @IsEnabled, @Hash, @CreatedAt, @UpdatedAt, @CreatedBy
                );
                SELECT last_insert_rowid();";

            var id = await conn.ExecuteScalarAsync<int>(sql, profile);
            Log.Information("ImportProfile created: {Name} (ID: {Id}) by {User}", profile.Name, id, createdByUserId);
            return id;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error creating ImportProfile {Name}", profile.Name);
            throw;
        }
    }

    // ── Update ─────────────────────────────────────────────────────────

    public async Task<bool> UpdateAsync(ImportProfile profile)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        try
        {
            await ValidateAsync(conn, profile);

            profile.Hash = _hashValidator.ComputeHash(profile);
            profile.UpdatedAt = DateTime.UtcNow;

            const string sql = @"
                UPDATE ImportProfiles SET
                    Name = @Name, GroupId = @GroupId,
                    SourceType = @SourceType, SourceDestinationId = @SourceDestinationId, SourceConfig = @SourceConfig,
                    SourceFilePath = @SourceFilePath, SourceFilePattern = @SourceFilePattern,
                    SourceFileSelection = @SourceFileSelection,
                    ArchiveAfterImport = @ArchiveAfterImport, ArchivePath = @ArchivePath,
                    HttpMethod = @HttpMethod, HttpBodyTemplate = @HttpBodyTemplate,
                    HttpPaginationEnabled = @HttpPaginationEnabled, HttpPaginationConfig = @HttpPaginationConfig,
                    HttpDataRootPath = @HttpDataRootPath,
                    SourceFormat = @SourceFormat, FormatConfig = @FormatConfig,
                    ColumnMappingsJson = @ColumnMappingsJson, AutoMapColumns = @AutoMapColumns,
                    SkipUnmappedColumns = @SkipUnmappedColumns,
                    TargetType = @TargetType,
                    TargetConnectionId = @TargetConnectionId, TargetTable = @TargetTable,
                    LocalTargetPath = @LocalTargetPath, LocalTargetFormat = @LocalTargetFormat,
                    LocalTargetWriteMode = @LocalTargetWriteMode,
                    LoadStrategy = @LoadStrategy, UpsertKeyColumns = @UpsertKeyColumns,
                    BatchSize = @BatchSize, CommandTimeoutSeconds = @CommandTimeoutSeconds,
                    DeltaSyncEnabled = @DeltaSyncEnabled, DeltaSyncReefIdColumn = @DeltaSyncReefIdColumn,
                    DeltaSyncHashAlgorithm = @DeltaSyncHashAlgorithm,
                    DeltaSyncTrackDeletes = @DeltaSyncTrackDeletes, DeltaSyncDeleteStrategy = @DeltaSyncDeleteStrategy,
                    DeltaSyncDeleteColumn = @DeltaSyncDeleteColumn, DeltaSyncDeleteValue = @DeltaSyncDeleteValue,
                    DeltaSyncRetentionDays = @DeltaSyncRetentionDays,
                    OnSourceFailure = @OnSourceFailure, OnParseFailure = @OnParseFailure,
                    OnRowFailure = @OnRowFailure, OnConstraintViolation = @OnConstraintViolation,
                    MaxFailedRowsBeforeAbort = @MaxFailedRowsBeforeAbort,
                    MaxFailedRowsPercent = @MaxFailedRowsPercent,
                    RollbackOnAbort = @RollbackOnAbort, RetryCount = @RetryCount,
                    PreProcessType = @PreProcessType, PreProcessConfig = @PreProcessConfig,
                    PreProcessRollbackOnFailure = @PreProcessRollbackOnFailure,
                    PostProcessType = @PostProcessType, PostProcessConfig = @PostProcessConfig,
                    PostProcessSkipOnFailure = @PostProcessSkipOnFailure,
                    NotificationConfig = @NotificationConfig,
                    IsEnabled = @IsEnabled, Hash = @Hash, UpdatedAt = @UpdatedAt
                WHERE Id = @Id";

            var rows = await conn.ExecuteAsync(sql, profile);
            if (rows > 0)
                Log.Information("ImportProfile updated: {Name} (#{Id})", profile.Name, profile.Id);
            return rows > 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error updating ImportProfile {Id}", profile.Id);
            throw;
        }
    }

    // ── Delete ─────────────────────────────────────────────────────────

    public async Task<bool> DeleteAsync(int id)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        try
        {
            var profile = await GetByIdAsync(id);
            if (profile is null) return false;

            var rows = await conn.ExecuteAsync("DELETE FROM ImportProfiles WHERE Id = @Id", new { Id = id });
            if (rows > 0)
                Log.Information("ImportProfile deleted: {Name} (ID: {Id})", profile.Name, id);
            return rows > 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error deleting ImportProfile {Id}", id);
            throw;
        }
    }

    // ── Enable / Disable ───────────────────────────────────────────────

    public async Task<bool> SetEnabledAsync(int id, bool enabled)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        var rows = await conn.ExecuteAsync(
            "UPDATE ImportProfiles SET IsEnabled = @IsEnabled, UpdatedAt = @UpdatedAt WHERE Id = @Id",
            new { IsEnabled = enabled, UpdatedAt = DateTime.UtcNow, Id = id });
        return rows > 0;
    }

    // ── Execution records ──────────────────────────────────────────────

    public async Task<int> CreateExecutionAsync(ImportProfileExecution exec)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        const string sql = @"
            INSERT INTO ImportProfileExecutions (
                ImportProfileId, Status, TriggeredBy, StartedAt
            ) VALUES (
                @ImportProfileId, @Status, @TriggeredBy, @StartedAt
            );
            SELECT last_insert_rowid();";

        return await conn.ExecuteScalarAsync<int>(sql, exec);
    }

    public async Task<bool> UpdateExecutionAsync(ImportProfileExecution exec)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        const string sql = @"
            UPDATE ImportProfileExecutions SET
                Status = @Status,
                CompletedAt = @CompletedAt,
                TotalRowsRead = @TotalRowsRead,
                RowsInserted = @RowsInserted,
                RowsUpdated = @RowsUpdated,
                RowsSkipped = @RowsSkipped,
                RowsDeleted = @RowsDeleted,
                RowsFailed = @RowsFailed,
                CurrentPhase = @CurrentPhase,
                ErrorMessage = @ErrorMessage,
                StackTrace = @StackTrace,
                ExecutionLog = @ExecutionLog,
                FilesProcessed = @FilesProcessed,
                BytesProcessed = @BytesProcessed,
                DeltaSyncNewRows = @DeltaSyncNewRows,
                DeltaSyncChangedRows = @DeltaSyncChangedRows,
                DeltaSyncUnchangedRows = @DeltaSyncUnchangedRows,
                DeltaSyncDeletedRows = @DeltaSyncDeletedRows,
                PhaseTimingsJson = @PhaseTimingsJson
            WHERE Id = @Id";

        var rows = await conn.ExecuteAsync(sql, exec);
        return rows > 0;
    }

    public async Task<List<ImportProfileExecution>> GetExecutionsAsync(int importProfileId, int limit = 50)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        const string sql = @"
            SELECT * FROM ImportProfileExecutions
            WHERE ImportProfileId = @ImportProfileId
            ORDER BY StartedAt DESC
            LIMIT @Limit";

        return (await conn.QueryAsync<ImportProfileExecution>(sql,
            new { ImportProfileId = importProfileId, Limit = limit })).ToList();
    }

    public async Task<ImportProfileExecution?> GetExecutionByIdAsync(int id)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        return await conn.QueryFirstOrDefaultAsync<ImportProfileExecution>(
            "SELECT * FROM ImportProfileExecutions WHERE Id = @Id", new { Id = id });
    }

    public async Task AddExecutionErrorAsync(ImportExecutionError error)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        const string sql = @"
            INSERT INTO ImportExecutionErrors (
                ExecutionId, RowNumber, ReefId, ErrorType, ErrorMessage, RowDataJson, Phase, OccurredAt
            ) VALUES (
                @ExecutionId, @RowNumber, @ReefId, @ErrorType, @ErrorMessage, @RowDataJson, @Phase, @OccurredAt
            )";

        await conn.ExecuteAsync(sql, error);
    }

    public async Task<List<ImportExecutionError>> GetExecutionErrorsAsync(int executionId)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        return (await conn.QueryAsync<ImportExecutionError>(
            "SELECT * FROM ImportExecutionErrors WHERE ExecutionId = @ExecutionId ORDER BY RowNumber",
            new { ExecutionId = executionId })).ToList();
    }

    public async Task UpdateLastExecutedAsync(int importProfileId)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync(
            "UPDATE ImportProfiles SET LastExecutedAt = @Now WHERE Id = @Id",
            new { Now = DateTime.UtcNow, Id = importProfileId });
    }

    public async Task<int> ResetDeltaSyncAsync(int importProfileId)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();
        var deleted = await conn.ExecuteAsync(
            "DELETE FROM ImportDeltaSyncState WHERE ImportProfileId = @Id",
            new { Id = importProfileId });
        Log.Information("Delta sync reset for ImportProfile {Id}: {Count} entries removed", importProfileId, deleted);
        return deleted;
    }

    public async Task<ImportDeltaSyncStats> GetDeltaSyncStatsAsync(int importProfileId)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync();

        var activeRows = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM ImportDeltaSyncState WHERE ImportProfileId = @Id AND IsDeleted = 0",
            new { Id = importProfileId });

        var deletedRows = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM ImportDeltaSyncState WHERE ImportProfileId = @Id AND IsDeleted = 1",
            new { Id = importProfileId });

        var lastSeenAt = await conn.ExecuteScalarAsync<string?>(
            "SELECT MAX(LastSeenAt) FROM ImportDeltaSyncState WHERE ImportProfileId = @Id",
            new { Id = importProfileId });

        var lastExec = await conn.QueryFirstOrDefaultAsync<ImportProfileExecution>(
            "SELECT * FROM ImportProfileExecutions WHERE ImportProfileId = @Id ORDER BY StartedAt DESC LIMIT 1",
            new { Id = importProfileId });

        return new ImportDeltaSyncStats
        {
            ActiveRows = activeRows,
            DeletedRows = deletedRows,
            TotalTrackedRows = activeRows + deletedRows,
            LastSyncDate = lastSeenAt,
            NewRowsLastRun = lastExec?.DeltaSyncNewRows ?? 0,
            ChangedRowsLastRun = lastExec?.DeltaSyncChangedRows ?? 0,
            DeletedRowsLastRun = lastExec?.DeltaSyncDeletedRows ?? 0,
            UnchangedRowsLastRun = lastExec?.DeltaSyncUnchangedRows ?? 0,
        };
    }

    // ── Validation ─────────────────────────────────────────────────────

    private static async Task ValidateAsync(SqliteConnection conn, ImportProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Name))
            throw new InvalidOperationException("ImportProfile Name is required");

        bool isLocalFile = profile.TargetType?.Equals("LocalFile", StringComparison.OrdinalIgnoreCase) == true;

        if (isLocalFile)
        {
            if (string.IsNullOrWhiteSpace(profile.LocalTargetPath))
                throw new InvalidOperationException("LocalTargetPath is required when TargetType is LocalFile");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(profile.TargetTable))
                throw new InvalidOperationException("TargetTable is required");

            // Validate target connection exists
            if (!profile.TargetConnectionId.HasValue || profile.TargetConnectionId.Value <= 0)
                throw new InvalidOperationException("TargetConnectionId is required for Database target");

            var connExists = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM Connections WHERE Id = @Id",
                new { Id = profile.TargetConnectionId.Value });

            if (connExists == 0)
                throw new InvalidOperationException($"Target Connection {profile.TargetConnectionId} not found");

            // Validate load strategy
            var validStrategies = new[] { "Insert", "Upsert", "FullReplace", "Append" };
            if (!validStrategies.Contains(profile.LoadStrategy, StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Invalid LoadStrategy '{profile.LoadStrategy}'");

            // Upsert requires key columns
            if (profile.LoadStrategy.Equals("Upsert", StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(profile.UpsertKeyColumns))
                throw new InvalidOperationException("UpsertKeyColumns is required when LoadStrategy is Upsert");
        }

        // Validate JSON fields
        foreach (var (field, value) in new[]
        {
            ("FormatConfig", profile.FormatConfig),
            ("ColumnMappingsJson", profile.ColumnMappingsJson),
            ("HttpPaginationConfig", profile.HttpPaginationConfig),
            ("NotificationConfig", profile.NotificationConfig)
        })
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                try { System.Text.Json.JsonDocument.Parse(value); }
                catch { throw new InvalidOperationException($"{field} must be valid JSON"); }
            }
        }

        if (profile.DeltaSyncEnabled)
        {
            if (string.IsNullOrWhiteSpace(profile.DeltaSyncReefIdColumn))
                throw new InvalidOperationException(
                    "Smart Sync requires a ReefId Column to be set.");
            if (!System.Text.RegularExpressions.Regex.IsMatch(
                    profile.DeltaSyncReefIdColumn,
                    @"^[a-zA-Z_][a-zA-Z0-9_]*$"))
                throw new InvalidOperationException(
                    "ReefId Column must be a valid identifier (letters, digits, underscores; cannot start with a digit).");
        }
    }

}
