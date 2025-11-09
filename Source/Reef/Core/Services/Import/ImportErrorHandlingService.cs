using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Serilog;
using Reef.Core.Models;
using ILogger = Serilog.ILogger;

namespace Reef.Core.Services.Import;

/// <summary>
/// Service for handling errors during import and quarantine failed rows
/// Supports multiple error handling strategies: Skip, Fail, Retry, Quarantine
/// </summary>
public class ImportErrorHandlingService
{
    private readonly string _connectionString;
    private static readonly ILogger Log = Serilog.Log.ForContext(typeof(ImportErrorHandlingService));

    public ImportErrorHandlingService(DatabaseConfig config)
    {
        _connectionString = config.ConnectionString;
    }

    /// <summary>
    /// Process row validation errors and apply error strategy
    /// </summary>
    public async Task<ErrorProcessingResult> ProcessRowErrorAsync(
        int importProfileId,
        int executionId,
        Dictionary<string, object> row,
        string errorMessage,
        ImportErrorStrategy strategy,
        CancellationToken cancellationToken = default)
    {
        var errorId = Guid.NewGuid();

        try
        {
            // Log the error
            await LogRowErrorAsync(importProfileId, executionId, errorId, row, errorMessage, cancellationToken);

            // Apply error strategy
            return strategy switch
            {
                ImportErrorStrategy.Skip => new ErrorProcessingResult
                {
                    Strategy = ImportErrorStrategy.Skip,
                    ShouldRetry = false,
                    ShouldQuarantine = false,
                    ShouldAbort = false,
                    ErrorId = errorId,
                    Message = "Row skipped due to error"
                },

                ImportErrorStrategy.Fail => new ErrorProcessingResult
                {
                    Strategy = ImportErrorStrategy.Fail,
                    ShouldRetry = false,
                    ShouldQuarantine = false,
                    ShouldAbort = true,
                    ErrorId = errorId,
                    Message = "Import aborted due to error"
                },

                ImportErrorStrategy.Retry => new ErrorProcessingResult
                {
                    Strategy = ImportErrorStrategy.Retry,
                    ShouldRetry = true,
                    ShouldQuarantine = false,
                    ShouldAbort = false,
                    ErrorId = errorId,
                    Message = "Row queued for retry"
                },

                ImportErrorStrategy.Quarantine => new ErrorProcessingResult
                {
                    Strategy = ImportErrorStrategy.Quarantine,
                    ShouldRetry = false,
                    ShouldQuarantine = true,
                    ShouldAbort = false,
                    ErrorId = errorId,
                    Message = "Row quarantined for review"
                },

                _ => new ErrorProcessingResult
                {
                    Strategy = ImportErrorStrategy.Skip,
                    ShouldRetry = false,
                    ShouldQuarantine = false,
                    ShouldAbort = false,
                    ErrorId = errorId,
                    Message = "Unknown strategy, row skipped"
                }
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to process row error for profile {ProfileId}", importProfileId);
            throw;
        }
    }

    /// <summary>
    /// Quarantine failed rows for manual review
    /// </summary>
    public async Task QuarantineRowAsync(
        int importProfileId,
        int executionId,
        Dictionary<string, object> row,
        string errorMessage,
        Guid? errorId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var db = new SqliteConnection(_connectionString);
            await db.OpenAsync(cancellationToken);

            var quarantineId = Guid.NewGuid();
            var rowDataJson = JsonSerializer.Serialize(row);

            const string sql = @"
                INSERT INTO ImportQuarantine (
                    Id, ImportProfileId, ExecutionId, RowData, ErrorMessage, ErrorId, CreatedAt, ReviewedAt
                ) VALUES (
                    @Id, @ProfileId, @ExecutionId, @RowData, @ErrorMessage, @ErrorId, @CreatedAt, NULL
                )";

            await db.ExecuteAsync(sql, new
            {
                Id = quarantineId.ToString(),
                ProfileId = importProfileId,
                ExecutionId = executionId,
                RowData = rowDataJson,
                ErrorMessage = errorMessage,
                ErrorId = errorId?.ToString(),
                CreatedAt = DateTime.UtcNow
            });

            Log.Information("Row quarantined: {QuarantineId} for profile {ProfileId}", quarantineId, importProfileId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to quarantine row for profile {ProfileId}", importProfileId);
            throw;
        }
    }

    /// <summary>
    /// Log detailed error information
    /// </summary>
    private async Task LogRowErrorAsync(
        int importProfileId,
        int executionId,
        Guid errorId,
        Dictionary<string, object> row,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            using var db = new SqliteConnection(_connectionString);
            await db.OpenAsync(cancellationToken);

            var rowDataJson = JsonSerializer.Serialize(row);
            var rowDataSummary = string.Join(", ", row.Keys.Take(3));

            const string sql = @"
                INSERT INTO ImportErrorLog (
                    Id, ImportProfileId, ExecutionId, ErrorMessage, RowDataSample, RowDataJson, CreatedAt
                ) VALUES (
                    @Id, @ProfileId, @ExecutionId, @ErrorMessage, @RowSample, @RowJson, @CreatedAt
                )";

            await db.ExecuteAsync(sql, new
            {
                Id = errorId.ToString(),
                ProfileId = importProfileId,
                ExecutionId = executionId,
                ErrorMessage = errorMessage,
                RowSample = rowDataSummary,
                RowJson = rowDataJson,
                CreatedAt = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to log error for profile {ProfileId}", importProfileId);
            // Don't throw - error logging shouldn't fail the import
        }
    }

    /// <summary>
    /// Get quarantined rows for a profile
    /// </summary>
    public async Task<List<QuarantinedRow>> GetQuarantinedRowsAsync(
        int importProfileId,
        int? limit = 100,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var db = new SqliteConnection(_connectionString);
            await db.OpenAsync(cancellationToken);

            var sql = @"
                SELECT Id, ImportProfileId, ExecutionId, RowData, ErrorMessage, ReviewedAt, CreatedAt
                FROM ImportQuarantine
                WHERE ImportProfileId = @ProfileId
                  AND ReviewedAt IS NULL
                ORDER BY CreatedAt DESC";

            if (limit.HasValue)
                sql += $" LIMIT {limit.Value}";

            var rows = await db.QueryAsync<QuarantinedRow>(sql, new { ProfileId = importProfileId });

            return rows.ToList();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to retrieve quarantined rows for profile {ProfileId}", importProfileId);
            return new List<QuarantinedRow>();
        }
    }

    /// <summary>
    /// Mark quarantined row as reviewed
    /// </summary>
    public async Task MarkQuarantineAsReviewedAsync(
        string quarantineId,
        string? action,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var db = new SqliteConnection(_connectionString);
            await db.OpenAsync(cancellationToken);

            const string sql = @"
                UPDATE ImportQuarantine
                SET ReviewedAt = @Now, ReviewAction = @Action
                WHERE Id = @Id";

            await db.ExecuteAsync(sql, new
            {
                Id = quarantineId,
                Now = DateTime.UtcNow,
                Action = action
            });

            Log.Information("Quarantine record {QuarantineId} marked as reviewed", quarantineId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to mark quarantine as reviewed: {QuarantineId}", quarantineId);
            throw;
        }
    }

    /// <summary>
    /// Get error statistics for an import execution
    /// </summary>
    public async Task<ErrorStatistics> GetErrorStatisticsAsync(
        int executionId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var db = new SqliteConnection(_connectionString);
            await db.OpenAsync(cancellationToken);

            var stats = await db.QueryFirstOrDefaultAsync<ErrorStatistics>(@"
                SELECT
                    COUNT(*) AS TotalErrors,
                    COUNT(DISTINCT ImportProfileId) AS AffectedProfiles
                FROM ImportErrorLog
                WHERE ExecutionId = @ExecutionId",
                new { ExecutionId = executionId });

            return stats ?? new ErrorStatistics();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to get error statistics for execution {ExecutionId}", executionId);
            return new ErrorStatistics();
        }
    }

    /// <summary>
    /// Clean up old quarantine records (older than specified days)
    /// </summary>
    public async Task CleanupOldQuarantineAsync(int retentionDays, CancellationToken cancellationToken = default)
    {
        try
        {
            using var db = new SqliteConnection(_connectionString);
            await db.OpenAsync(cancellationToken);

            var cutoffDate = DateTime.UtcNow.AddDays(-retentionDays);

            var deleted = await db.ExecuteAsync(@"
                DELETE FROM ImportQuarantine
                WHERE ReviewedAt IS NOT NULL
                  AND CreatedAt < @CutoffDate",
                new { CutoffDate = cutoffDate });

            if (deleted > 0)
                Log.Information("Cleaned up {Count} old quarantine records", deleted);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to cleanup old quarantine records");
        }
    }
}

/// <summary>
/// Result of error processing
/// </summary>
public class ErrorProcessingResult
{
    public ImportErrorStrategy Strategy { get; set; }
    public bool ShouldRetry { get; set; }
    public bool ShouldQuarantine { get; set; }
    public bool ShouldAbort { get; set; }
    public Guid ErrorId { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Quarantined row for manual review
/// </summary>
public class QuarantinedRow
{
    public string Id { get; set; } = string.Empty;
    public int ImportProfileId { get; set; }
    public int ExecutionId { get; set; }
    public string RowData { get; set; } = string.Empty;
    public string ErrorMessage { get; set; } = string.Empty;
    public DateTime? ReviewedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Error statistics for an import execution
/// </summary>
public class ErrorStatistics
{
    public int TotalErrors { get; set; }
    public int AffectedProfiles { get; set; }
}
