using Microsoft.Data.Sqlite;
using Dapper;
using Serilog;
using Reef.Core.Abstractions;
using Reef.Core.Models;
using Reef.Core.Services.Import.DataSourceExecutors;
using Reef.Core.Services.Import.Writers;
using Reef.Core.Security;
using ILogger = Serilog.ILogger;

namespace Reef.Core.Services.Import;

/// <summary>
/// Orchestrates the import execution pipeline (9-stage process)
/// </summary>
public class ImportExecutionService : IImportExecutionService
{
    private readonly string _connectionString;
    private readonly IImportProfileService _profileService;
    private readonly EncryptionService _encryptionService;
    private readonly ImportDeltaSyncService _deltaSyncService;
    private readonly ImportTransformationService _transformationService;
    private static readonly ILogger Log = Serilog.Log.ForContext(typeof(ImportExecutionService));

    // Storage for delta sync result during pipeline execution
    private ImportDeltaSyncResult? _deltaSyncResult;

    public ImportExecutionService(
        DatabaseConfig config,
        IImportProfileService profileService,
        EncryptionService encryptionService)
    {
        _connectionString = config.ConnectionString;
        _profileService = profileService;
        _encryptionService = encryptionService;
        _deltaSyncService = new ImportDeltaSyncService(config);
        _transformationService = new ImportTransformationService();
    }

    /// <summary>
    /// Executes a complete import profile through the 9-stage pipeline
    /// Stages: 1-Validate, 2-SourceRead, 3-Transform, 4-Validate, 5-Write, 6-Commit, 7-Cleanup, 8-Log, 9-Notify
    /// OPTIMIZATION: Load profile once at start and pass through all stages (eliminates 5+ database lookups)
    /// </summary>
    public async Task<ImportExecutionResult> ExecuteAsync(
        int profileId,
        string triggeredBy = "Manual",
        CancellationToken cancellationToken = default)
    {
        var execution = new ImportExecution
        {
            ImportProfileId = profileId,
            StartedAt = DateTime.UtcNow,
            Status = "Running",
            TriggeredBy = triggeredBy
        };

        var watch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            Log.Information("Import execution started for profile {ProfileId} triggered by {TriggeredBy}", profileId, triggeredBy);

            // Load profile ONCE at the start - OPTIMIZATION: Eliminates repeated lookups
            var profile = await _profileService.GetByIdAsync(profileId, cancellationToken);
            if (profile == null)
                throw new InvalidOperationException($"Profile {profileId} not found");

            Log.Debug("Profile loaded for {ProfileId}: source={Source}, destination={Destination}",
                profileId, profile.SourceType, profile.DestinationType);

            // Stage 1: Validate Configuration
            execution.CurrentStage = "Validation";
            await StageValidateAsync(profile, execution, cancellationToken);

            // Stage 2: Read from Source
            execution.CurrentStage = "SourceRead";
            var rows = await StageSourceReadAsync(profile, execution, cancellationToken);

            if (rows.Count == 0)
            {
                Log.Warning("No rows read from source for profile {ProfileId}", profileId);
                execution.RowsRead = 0;
                execution.RowsWritten = 0;
                execution.Status = "Success";
                execution.CompletedAt = DateTime.UtcNow;
                watch.Stop();
                execution.ExecutionTimeMs = watch.ElapsedMilliseconds;
                await SaveExecutionAsync(execution, cancellationToken);
                return MapToResult(execution);
            }

            execution.RowsRead = rows.Count;
            Log.Information("Read {Count} rows from source", rows.Count);

            // Stage 2.5: Delta Sync (NEW - Phase 2)
            execution.CurrentStage = "DeltaSync";
            _deltaSyncResult = await StageDeltaSyncAsync(profileId, rows, profile, execution, cancellationToken);

            // Only process rows that are new or changed
            var rowsToProcess = _deltaSyncResult.NewRows.Concat(_deltaSyncResult.ChangedRows).ToList();
            execution.DeltaSyncNewRows = _deltaSyncResult.NewRows.Count;
            execution.DeltaSyncChangedRows = _deltaSyncResult.ChangedRows.Count;
            execution.DeltaSyncUnchangedRows = _deltaSyncResult.UnchangedRows.Count;

            Log.Information("Delta sync: {New} new, {Changed} changed, {Unchanged} unchanged rows",
                _deltaSyncResult.NewRows.Count, _deltaSyncResult.ChangedRows.Count, _deltaSyncResult.UnchangedRows.Count);

            rows = rowsToProcess;

            // Stage 3: Transform Data
            execution.CurrentStage = "Transform";
            rows = await StageTransformAsync(profile, rows, execution, cancellationToken);

            // Stage 4: Validate Data
            execution.CurrentStage = "Validate";
            var validationResult = await StageValidateDataAsync(profile, rows, execution, cancellationToken);
            rows = validationResult.ValidRows;
            execution.RowsSkipped = validationResult.InvalidRowCount;

            // Stage 5: Write to Destination
            execution.CurrentStage = "Write";
            var writeResult = await StageWriteAsync(profile, rows, execution, cancellationToken);
            execution.RowsWritten = writeResult.RowsWritten;
            execution.RowsFailed = writeResult.RowsFailed;

            // Stage 6: Commit
            execution.CurrentStage = "Commit";
            await StageCommitAsync(profileId, profile, execution, cancellationToken);

            // Stage 7: Cleanup
            execution.CurrentStage = "Cleanup";
            await StageCleanupAsync(profileId, execution, cancellationToken);

            // Stage 8: Log
            execution.CurrentStage = "Log";
            await StageLogAsync(profileId, execution, cancellationToken);

            execution.Status = "Success";
            execution.CompletedAt = DateTime.UtcNow;
            watch.Stop();
            execution.ExecutionTimeMs = watch.ElapsedMilliseconds;

            Log.Information("Import execution completed: {ProfileId} - {RowsRead} read, {RowsWritten} written, {RowsSkipped} skipped in {ElapsedMs}ms",
                profileId, execution.RowsRead, execution.RowsWritten, execution.RowsSkipped, watch.ElapsedMilliseconds);

            await SaveExecutionAsync(execution, cancellationToken);
            return MapToResult(execution);
        }
        catch (OperationCanceledException)
        {
            execution.Status = "Cancelled";
            execution.ErrorMessage = "Execution was cancelled";
            watch.Stop();
            execution.ExecutionTimeMs = watch.ElapsedMilliseconds;
            await SaveExecutionAsync(execution, cancellationToken);
            throw;
        }
        catch (Exception ex)
        {
            execution.Status = "Failed";
            execution.ErrorMessage = ex.Message;
            execution.ErrorDetails = ex.StackTrace;
            execution.CompletedAt = DateTime.UtcNow;
            watch.Stop();
            execution.ExecutionTimeMs = watch.ElapsedMilliseconds;

            Log.Error(ex, "Import execution failed for profile {ProfileId}", profileId);

            await SaveExecutionAsync(execution, cancellationToken);
            return MapToResult(execution);
        }
    }

    /// <summary>
    /// Cancels a running import execution
    /// </summary>
    public async Task<bool> CancelAsync(int executionId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            const string sql = @"
                UPDATE ImportExecutions
                SET Status = 'Cancelled', CompletedAt = @CompletedAt
                WHERE Id = @Id AND Status = 'Running'";

            var rowsAffected = await connection.ExecuteAsync(sql, new
            {
                Id = executionId,
                CompletedAt = DateTime.UtcNow
            });

            return rowsAffected > 0;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error cancelling execution {ExecutionId}", executionId);
            return false;
        }
    }

    // ===== Pipeline Stages =====

    private async Task StageValidateAsync(ImportProfile profile, ImportExecution execution, CancellationToken cancellationToken)
    {
        // OPTIMIZATION: Profile is already loaded, no need for lookup
        if (profile == null)
            throw new InvalidOperationException($"Profile is null");

        if (!profile.IsEnabled)
            throw new InvalidOperationException($"Profile {profile.Id} is disabled");

        Log.Debug("Stage 1 (Validate) completed for profile {ProfileId}", profile.Id);
        await Task.CompletedTask;
    }

    private async Task<List<Dictionary<string, object>>> StageSourceReadAsync(
        ImportProfile profile, ImportExecution execution, CancellationToken cancellationToken)
    {
        // OPTIMIZATION: Profile is already loaded, no need for lookup
        if (profile == null)
            throw new InvalidOperationException($"Profile is null");

        IDataSourceExecutor executor = profile.SourceType switch
        {
            DataSourceType.RestApi => new RestDataSourceExecutor(),
            DataSourceType.S3 => new S3DataSourceExecutor(),
            DataSourceType.Ftp => new FtpDataSourceExecutor(),
            DataSourceType.Database => new DatabaseDataSourceExecutor(_encryptionService),
            DataSourceType.Sftp => new FtpDataSourceExecutor(),
            _ => throw new NotSupportedException($"Source type {profile.SourceType} not yet implemented")
        };

        var rows = await executor.ExecuteAsync(profile.SourceUri, profile.SourceConfiguration, cancellationToken);
        Log.Debug("Stage 2 (SourceRead) completed: {Count} rows read for profile {ProfileId}", rows.Count, profile.Id);
        return rows;
    }

    /// <summary>
    /// Stage 2.5: Delta Sync - Detect new, changed, and unchanged rows from source
    /// OPTIMIZATION: Profile is already loaded, no need for lookup
    /// </summary>
    private async Task<ImportDeltaSyncResult> StageDeltaSyncAsync(
        int profileId,
        List<Dictionary<string, object>> sourceRows,
        ImportProfile profile,
        ImportExecution execution,
        CancellationToken cancellationToken)
    {
        // OPTIMIZATION: Profile is already loaded, no need for lookup
        if (profile == null)
            throw new InvalidOperationException($"Profile is null");

        try
        {
            var result = await _deltaSyncService.ProcessDeltaAsync(profileId, sourceRows, profile, cancellationToken);
            Log.Debug("Stage 2.5 (DeltaSync) completed for profile {ProfileId}", profileId);
            return result;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Delta sync processing failed for profile {ProfileId}, treating all rows as new", profileId);
            // Fallback: treat all rows as new if delta sync fails
            return new ImportDeltaSyncResult
            {
                NewRows = sourceRows,
                ChangedRows = new List<Dictionary<string, object>>(),
                UnchangedRows = new List<Dictionary<string, object>>(),
                TotalRowsProcessed = sourceRows.Count,
                DeltaSyncEnabled = false
            };
        }
    }

    private async Task<List<Dictionary<string, object>>> StageTransformAsync(
        ImportProfile profile,
        List<Dictionary<string, object>> rows,
        ImportExecution execution,
        CancellationToken cancellationToken)
    {
        // OPTIMIZATION: Profile is already loaded, no need for lookup
        if (profile == null)
            throw new InvalidOperationException($"Profile is null");

        try
        {
            var transformedRows = await _transformationService.TransformRowsAsync(rows, profile, cancellationToken);
            Log.Debug("Stage 3 (Transform) completed for profile {ProfileId}: {Count} rows transformed", profile.Id, transformedRows.Count);
            return transformedRows;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Transformation failed for profile {ProfileId}, using source rows as-is", profile.Id);
            // Continue with source rows if transformation fails
            return rows;
        }
    }

    private Task<(List<Dictionary<string, object>> ValidRows, int InvalidRowCount)> StageValidateDataAsync(
        ImportProfile profile,
        List<Dictionary<string, object>> rows,
        ImportExecution execution,
        CancellationToken cancellationToken)
    {
        // OPTIMIZATION: Profile is already loaded, no need for lookup
        if (profile == null)
            throw new InvalidOperationException($"Profile is null");

        // TODO: Implement validation rules in Phase 2
        Log.Debug("Stage 4 (ValidateData) skipped for profile {ProfileId} (no validation rules defined)", profile.Id);
        return Task.FromResult((rows, 0));
    }

    private async Task<WriteResult> StageWriteAsync(
        ImportProfile profile,
        List<Dictionary<string, object>> rows,
        ImportExecution execution,
        CancellationToken cancellationToken)
    {
        // OPTIMIZATION: Profile is already loaded, no need for lookup
        if (profile == null)
            throw new InvalidOperationException($"Profile is null");

        IDataWriter writer = profile.DestinationType switch
        {
            ImportDestinationType.Database => new DatabaseWriter(_encryptionService),
            _ => throw new NotSupportedException($"Destination type {profile.DestinationType} not yet implemented")
        };

        var fieldMappings = new List<FieldMapping>();  // TODO: Parse from FieldMappingsJson
        var writeMode = WriteMode.Insert;  // TODO: Determine from config

        var result = await writer.WriteAsync(profile.DestinationUri, profile.DestinationConfiguration, rows, fieldMappings, writeMode, cancellationToken);
        Log.Debug("Stage 5 (Write) completed: {Written} rows written for profile {ProfileId}", result.RowsWritten, profile.Id);
        return result;
    }

    private async Task StageCommitAsync(int profileId, ImportProfile profile, ImportExecution execution, CancellationToken cancellationToken)
    {
        // OPTIMIZATION: Profile is already loaded, no need for lookup
        if (profile == null)
            throw new InvalidOperationException($"Profile is null");

        // Commit delta sync state if enabled and we have results
        if (_deltaSyncResult != null && _deltaSyncResult.DeltaSyncEnabled && profile.DeltaSyncMode != DeltaSyncMode.None)
        {
            try
            {
                await _deltaSyncService.CommitDeltaSyncAsync(
                    profileId,
                    execution.Id,
                    _deltaSyncResult.NewRows,
                    _deltaSyncResult.ChangedRows,
                    profile,
                    cancellationToken);

                Log.Debug("Delta sync state committed for profile {ProfileId}", profileId);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to commit delta sync state for profile {ProfileId}", profileId);
                // Don't fail the entire execution if delta sync commit fails
                // The data has already been written to the destination
            }
        }

        Log.Debug("Stage 6 (Commit) completed for profile {ProfileId}", profileId);
    }

    private async Task StageCleanupAsync(int profileId, ImportExecution execution, CancellationToken cancellationToken)
    {
        // TODO: Implement cleanup (temporary files, error logs) in Phase 2
        Log.Debug("Stage 7 (Cleanup) completed for profile {ProfileId}", profileId);
        await Task.CompletedTask;
    }

    private async Task StageLogAsync(int profileId, ImportExecution execution, CancellationToken cancellationToken)
    {
        // TODO: Implement audit logging in Phase 2
        Log.Debug("Stage 8 (Log) completed for profile {ProfileId}", profileId);
        await Task.CompletedTask;
    }

    // ===== Helper Methods =====

    private async Task SaveExecutionAsync(ImportExecution execution, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            const string sql = @"
                INSERT INTO ImportExecutions (
                    ImportProfileId, JobId,
                    StartedAt, CompletedAt, ExecutionTimeMs,
                    Status,
                    RowsRead, RowsWritten, RowsSkipped, RowsFailed,
                    DeltaSyncNewRows, DeltaSyncChangedRows, DeltaSyncUnchangedRows,
                    ErrorMessage, ErrorDetails,
                    CurrentStage, StageDetails,
                    TriggeredBy
                ) VALUES (
                    @ImportProfileId, @JobId,
                    @StartedAt, @CompletedAt, @ExecutionTimeMs,
                    @Status,
                    @RowsRead, @RowsWritten, @RowsSkipped, @RowsFailed,
                    @DeltaSyncNewRows, @DeltaSyncChangedRows, @DeltaSyncUnchangedRows,
                    @ErrorMessage, @ErrorDetails,
                    @CurrentStage, @StageDetails,
                    @TriggeredBy
                )";

            await connection.ExecuteAsync(sql, execution);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save execution record");
        }
    }

    private ImportExecutionResult MapToResult(ImportExecution execution)
    {
        return new ImportExecutionResult
        {
            ExecutionId = execution.Id,
            Status = execution.Status,
            RowsRead = execution.RowsRead,
            RowsWritten = execution.RowsWritten,
            RowsSkipped = execution.RowsSkipped,
            RowsFailed = execution.RowsFailed,
            ExecutionTimeMs = execution.ExecutionTimeMs,
            ErrorMessage = execution.ErrorMessage
        };
    }
}
