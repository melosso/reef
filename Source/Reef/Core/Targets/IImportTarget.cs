using Reef.Core.Models;

namespace Reef.Core.Targets;

/// <summary>
/// Abstraction for writing imported rows into a target database.
/// </summary>
public interface IImportTarget
{
    /// <summary>
    /// Write a batch of rows to the target table.
    /// Respects LoadStrategy (Insert/Upsert/Append) set in the context.
    /// </summary>
    Task<ImportBatchResult> WriteBatchAsync(
        IReadOnlyList<Dictionary<string, object?>> rows,
        ImportWriteContext context,
        CancellationToken ct = default);

    /// <summary>
    /// Execute a full-replace operation: TRUNCATE then INSERT all rows atomically.
    /// </summary>
    Task<ImportBatchResult> FullReplaceAsync(
        List<Dictionary<string, object?>> rows,
        ImportWriteContext context,
        CancellationToken ct = default);

    /// <summary>
    /// Apply a delete strategy to rows that disappeared from the source.
    /// Used by delta sync when DeltaSyncTrackDeletes = true.
    /// </summary>
    Task<int> ApplyDeletesAsync(
        IReadOnlyList<string> deletedReefIds,
        string reefIdColumn,
        ImportProfile profile,
        CancellationToken ct = default);

    /// <summary>
    /// Retrieve the column schema of the target table.
    /// Used to drive auto-mapping and the UI schema preview.
    /// </summary>
    Task<List<TargetColumnInfo>> GetTableSchemaAsync(
        Connection connection,
        string tableName,
        CancellationToken ct = default);

    /// <summary>
    /// Test that the target connection is reachable and the table exists.
    /// </summary>
    Task<(bool Success, string? Message)> TestAsync(
        Connection connection,
        string tableName,
        CancellationToken ct = default);
}
