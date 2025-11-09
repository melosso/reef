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
    private static readonly ILogger Log = Serilog.Log.ForContext(typeof(ImportExecutionService));

    public ImportExecutionService(
        DatabaseConfig config,
        IImportProfileService profileService,
        EncryptionService encryptionService)
    {
        _connectionString = config.ConnectionString;
        _profileService = profileService;
        _encryptionService = encryptionService;
    }

    /// <summary>
    /// Executes a complete import profile through the 9-stage pipeline
    /// Stages: 1-Validate, 2-SourceRead, 3-Transform, 4-Validate, 5-Write, 6-Commit, 7-Cleanup, 8-Log, 9-Notify
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

            // Stage 1: Validate Configuration
            execution.CurrentStage = "Validation";
            await StageValidateAsync(profileId, execution, cancellationToken);

            // Stage 2: Read from Source
            execution.CurrentStage = "SourceRead";
            var rows = await StageSourceReadAsync(profileId, execution, cancellationToken);

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

            // Stage 3: Transform Data
            execution.CurrentStage = "Transform";
            rows = await StageTransformAsync(profileId, rows, execution, cancellationToken);

            // Stage 4: Validate Data
            execution.CurrentStage = "Validate";
            var validationResult = await StageValidateDataAsync(profileId, rows, execution, cancellationToken);
            rows = validationResult.ValidRows;
            execution.RowsSkipped = validationResult.InvalidRowCount;

            // Stage 5: Write to Destination
            execution.CurrentStage = "Write";
            var writeResult = await StageWriteAsync(profileId, rows, execution, cancellationToken);
            execution.RowsWritten = writeResult.RowsWritten;
            execution.RowsFailed = writeResult.RowsFailed;

            // Stage 6: Commit
            execution.CurrentStage = "Commit";
            await StageCommitAsync(profileId, execution, cancellationToken);

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

    private async Task StageValidateAsync(int profileId, ImportExecution execution, CancellationToken cancellationToken)
    {
        var profile = await _profileService.GetByIdAsync(profileId, cancellationToken);
        if (profile == null)
            throw new InvalidOperationException($"Profile {profileId} not found");

        if (!profile.IsEnabled)
            throw new InvalidOperationException($"Profile {profileId} is disabled");

        Log.Debug("Stage 1 (Validate) completed for profile {ProfileId}", profileId);
    }

    private async Task<List<Dictionary<string, object>>> StageSourceReadAsync(
        int profileId, ImportExecution execution, CancellationToken cancellationToken)
    {
        var profile = await _profileService.GetByIdAsync(profileId, cancellationToken);
        if (profile == null)
            throw new InvalidOperationException($"Profile {profileId} not found");

        IDataSourceExecutor executor = profile.SourceType switch
        {
            DataSourceType.RestApi => new RestDataSourceExecutor(),
            _ => throw new NotSupportedException($"Source type {profile.SourceType} not yet implemented")
        };

        var rows = await executor.ExecuteAsync(profile.SourceUri, profile.SourceConfiguration, cancellationToken);
        Log.Debug("Stage 2 (SourceRead) completed: {Count} rows read for profile {ProfileId}", rows.Count, profileId);
        return rows;
    }

    private Task<List<Dictionary<string, object>>> StageTransformAsync(
        int profileId,
        List<Dictionary<string, object>> rows,
        ImportExecution execution,
        CancellationToken cancellationToken)
    {
        // TODO: Implement Scriban template transformations in Phase 2
        Log.Debug("Stage 3 (Transform) skipped for profile {ProfileId} (no transformations defined)", profileId);
        return Task.FromResult(rows);
    }

    private Task<(List<Dictionary<string, object>> ValidRows, int InvalidRowCount)> StageValidateDataAsync(
        int profileId,
        List<Dictionary<string, object>> rows,
        ImportExecution execution,
        CancellationToken cancellationToken)
    {
        // TODO: Implement validation rules in Phase 2
        Log.Debug("Stage 4 (ValidateData) skipped for profile {ProfileId} (no validation rules defined)", profileId);
        return Task.FromResult((rows, 0));
    }

    private async Task<WriteResult> StageWriteAsync(
        int profileId,
        List<Dictionary<string, object>> rows,
        ImportExecution execution,
        CancellationToken cancellationToken)
    {
        var profile = await _profileService.GetByIdAsync(profileId, cancellationToken);
        if (profile == null)
            throw new InvalidOperationException($"Profile {profileId} not found");

        IDataWriter writer = profile.DestinationType switch
        {
            ImportDestinationType.Database => new DatabaseWriter(_encryptionService),
            _ => throw new NotSupportedException($"Destination type {profile.DestinationType} not yet implemented")
        };

        var fieldMappings = new List<FieldMapping>();  // TODO: Parse from FieldMappingsJson
        var writeMode = WriteMode.Insert;  // TODO: Determine from config

        var result = await writer.WriteAsync(profile.DestinationUri, profile.DestinationConfiguration, rows, fieldMappings, writeMode, cancellationToken);
        Log.Debug("Stage 5 (Write) completed: {Written} rows written for profile {ProfileId}", result.RowsWritten, profileId);
        return result;
    }

    private async Task StageCommitAsync(int profileId, ImportExecution execution, CancellationToken cancellationToken)
    {
        // TODO: Implement commit logic in Phase 2 (delta sync state update)
        Log.Debug("Stage 6 (Commit) completed for profile {ProfileId}", profileId);
        await Task.CompletedTask;
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
