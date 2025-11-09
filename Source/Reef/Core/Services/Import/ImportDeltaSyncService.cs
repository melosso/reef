using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;
using Reef.Core.Models;
using Serilog;

namespace Reef.Core.Services.Import;

/// <summary>
/// Service for managing delta sync operations for imports
/// Handles hash calculation, comparison, and state management for import profiles
/// Detects which rows are new, changed, or unchanged from the source
/// </summary>
public class ImportDeltaSyncService
{
    private readonly string _connectionString;

    public ImportDeltaSyncService(DatabaseConfig config)
    {
        _connectionString = config.ConnectionString;
    }

    /// <summary>
    /// Calculate hash for a single row (used for import delta sync)
    /// </summary>
    public async Task<string> CalculateRowHashAsync(
        Dictionary<string, object> row,
        string keyColumn,
        string hashAlgorithm = "SHA256")
    {
        return await Task.Run(() =>
        {
            var sb = new StringBuilder();

            // Include key column in hash to prevent collisions
            if (row.TryGetValue(keyColumn, out var keyValue))
            {
                sb.Append($"KEY:{keyValue}|");
            }

            // Sort keys for consistent ordering
            var sortedKeys = row.Keys.OrderBy(k => k).ToList();

            foreach (var key in sortedKeys)
            {
                var value = NormalizeValueForHash(row[key]);
                sb.Append($"{key}={value};");
            }

            // Calculate hash
            var hashInput = sb.ToString();
            return CalculateHash(hashInput, hashAlgorithm);
        });
    }

    /// <summary>
    /// Process rows from source and determine deltas based on previous state
    /// Returns classification of rows as new, changed, or unchanged
    /// </summary>
    public async Task<ImportDeltaSyncResult> ProcessDeltaAsync(
        int importProfileId,
        List<Dictionary<string, object>> sourceRows,
        ImportProfile profile,
        CancellationToken cancellationToken = default)
    {
        if (profile.DeltaSyncMode == DeltaSyncMode.None)
        {
            // Delta sync disabled - all rows are considered "new"
            return new ImportDeltaSyncResult
            {
                NewRows = sourceRows,
                ChangedRows = new List<Dictionary<string, object>>(),
                UnchangedRows = new List<Dictionary<string, object>>(),
                TotalRowsProcessed = sourceRows.Count,
                DeltaSyncEnabled = false
            };
        }

        // Validate configuration
        if (string.IsNullOrWhiteSpace(profile.DeltaSyncKeyColumns))
        {
            throw new InvalidOperationException(
                $"Delta sync enabled but no key columns specified for profile {importProfileId}");
        }

        var keyColumns = profile.DeltaSyncKeyColumns.Split(',')
            .Select(c => c.Trim())
            .ToList();

        if (keyColumns.Count == 0)
        {
            throw new InvalidOperationException(
                $"Invalid key columns specification for profile {importProfileId}");
        }

        // Validate that key columns exist in source data
        if (sourceRows.Any())
        {
            var firstRow = sourceRows.First();
            var missingColumns = keyColumns.Where(kc => !firstRow.ContainsKey(kc)).ToList();
            if (missingColumns.Any())
            {
                throw new InvalidOperationException(
                    $"Key columns not found in source data: {string.Join(", ", missingColumns)}");
            }
        }

        var result = new ImportDeltaSyncResult
        {
            NewRows = new List<Dictionary<string, object>>(),
            ChangedRows = new List<Dictionary<string, object>>(),
            UnchangedRows = new List<Dictionary<string, object>>(),
            TotalRowsProcessed = sourceRows.Count,
            DeltaSyncEnabled = true,
            KeyColumns = keyColumns,
            Mode = profile.DeltaSyncMode
        };

        // Get previous hash state for this profile
        var previousHashes = await GetPreviousHashStateAsync(importProfileId, cancellationToken);

        // Process each row
        foreach (var row in sourceRows)
        {
            // Create composite key from key columns
            var keyValue = CreateCompositeKey(row, keyColumns);

            // Calculate current hash
            var currentHash = await CalculateRowHashAsync(row, keyColumns[0], "SHA256");

            if (!previousHashes.ContainsKey(keyValue))
            {
                // New row - not seen before
                result.NewRows.Add(row);
            }
            else if (previousHashes[keyValue] != currentHash)
            {
                // Changed row - hash different from last time
                result.ChangedRows.Add(row);
            }
            else
            {
                // Unchanged row - same hash as before
                result.UnchangedRows.Add(row);
            }
        }

        Log.Information(
            "Delta sync for import profile {ProfileId}: {New} new, {Changed} changed, {Unchanged} unchanged rows",
            importProfileId, result.NewRows.Count, result.ChangedRows.Count, result.UnchangedRows.Count);

        return result;
    }

    /// <summary>
    /// Commit delta sync state after successful import
    /// Should ONLY be called after successful transformation and write
    /// </summary>
    public async Task CommitDeltaSyncAsync(
        int importProfileId,
        int executionId,
        List<Dictionary<string, object>> newRows,
        List<Dictionary<string, object>> changedRows,
        ImportProfile profile,
        CancellationToken cancellationToken = default)
    {
        if (profile.DeltaSyncMode == DeltaSyncMode.None)
        {
            Log.Debug("Delta sync disabled for profile {ProfileId}, skipping state commit", importProfileId);
            return;
        }

        var keyColumns = profile.DeltaSyncKeyColumns!.Split(',')
            .Select(c => c.Trim())
            .ToList();

        var rowsToCommit = new List<(string key, string hash)>();

        // Process new rows
        foreach (var row in newRows)
        {
            var keyValue = CreateCompositeKey(row, keyColumns);
            var hash = await CalculateRowHashAsync(row, keyColumns[0], "SHA256");
            rowsToCommit.Add((keyValue, hash));
        }

        // Process changed rows
        foreach (var row in changedRows)
        {
            var keyValue = CreateCompositeKey(row, keyColumns);
            var hash = await CalculateRowHashAsync(row, keyColumns[0], "SHA256");
            rowsToCommit.Add((keyValue, hash));
        }

        await UpdateHashStateAsync(importProfileId, executionId, rowsToCommit, cancellationToken);

        Log.Information(
            "Committed delta sync state for import profile {ProfileId}: {Count} rows tracked",
            importProfileId, rowsToCommit.Count);
    }

    /// <summary>
    /// Get previous hash state for an import profile
    /// </summary>
    public async Task<Dictionary<string, string>> GetPreviousHashStateAsync(
        int importProfileId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var db = new SqliteConnection(_connectionString);
            await db.OpenAsync(cancellationToken);

            var rows = await db.QueryAsync<(string CompositeKey, string RowHash)>(
                @"SELECT CompositeKey, RowHash
                  FROM ImportDeltaSyncState
                  WHERE ImportProfileId = @ProfileId",
                new { ProfileId = importProfileId });

            return rows.ToDictionary(r => r.CompositeKey, r => r.RowHash);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to retrieve previous delta sync state for profile {ProfileId}, treating as first run", importProfileId);
            return new Dictionary<string, string>();
        }
    }

    /// <summary>
    /// Update hash state after successful import
    /// </summary>
    private async Task UpdateHashStateAsync(
        int importProfileId,
        int executionId,
        List<(string key, string hash)> rows,
        CancellationToken cancellationToken = default)
    {
        using var db = new SqliteConnection(_connectionString);
        await db.OpenAsync(cancellationToken);

        using var transaction = await db.BeginTransactionAsync(cancellationToken);
        try
        {
            // First, clear old state for this profile
            await db.ExecuteAsync(
                "DELETE FROM ImportDeltaSyncState WHERE ImportProfileId = @ProfileId",
                new { ProfileId = importProfileId },
                transaction);

            // Process in batches
            const int batchSize = 1000;
            var entries = rows.ToList();

            for (int i = 0; i < entries.Count; i += batchSize)
            {
                var batch = entries.Skip(i).Take(batchSize).ToList();

                foreach (var (key, hash) in batch)
                {
                    await db.ExecuteAsync(
                        @"INSERT INTO ImportDeltaSyncState
                          (ImportProfileId, CompositeKey, RowHash, LastSeenExecutionId, LastSeenAt)
                          VALUES (@ProfileId, @Key, @Hash, @ExecutionId, @Now)",
                        new
                        {
                            ProfileId = importProfileId,
                            Key = key,
                            Hash = hash,
                            ExecutionId = executionId,
                            Now = DateTime.UtcNow
                        },
                        transaction);
                }
            }

            await transaction.CommitAsync(cancellationToken);
            Log.Debug("Updated import delta sync state for {Count} rows in profile {ProfileId}",
                rows.Count, importProfileId);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            Log.Error(ex, "Failed to update import delta sync state for profile {ProfileId}", importProfileId);
            throw;
        }
    }

    /// <summary>
    /// Reset delta sync state for an import profile
    /// </summary>
    public async Task ResetDeltaSyncStateAsync(int importProfileId, CancellationToken cancellationToken = default)
    {
        using var db = new SqliteConnection(_connectionString);
        await db.OpenAsync(cancellationToken);

        await db.ExecuteAsync(
            "DELETE FROM ImportDeltaSyncState WHERE ImportProfileId = @ProfileId",
            new { ProfileId = importProfileId });

        Log.Information("Reset delta sync state for import profile {ProfileId}", importProfileId);
    }

    /// <summary>
    /// Get delta sync statistics for an import profile
    /// </summary>
    public async Task<ImportDeltaSyncStats> GetProfileStatsAsync(
        int importProfileId,
        CancellationToken cancellationToken = default)
    {
        using var db = new SqliteConnection(_connectionString);
        await db.OpenAsync(cancellationToken);

        var stats = await db.QueryFirstOrDefaultAsync<ImportDeltaSyncStats>(
            @"SELECT
                COUNT(*) AS TotalTrackedRows,
                MIN(LastSeenAt) AS FirstTrackedAt,
                MAX(LastSeenAt) AS LastTrackedAt
              FROM ImportDeltaSyncState
              WHERE ImportProfileId = @ProfileId",
            new { ProfileId = importProfileId });

        return stats ?? new ImportDeltaSyncStats();
    }

    // ===== Private Helper Methods =====

    private string CalculateHash(string input, string algorithm)
    {
        byte[] hashBytes = algorithm.ToUpperInvariant() switch
        {
            "SHA256" => SHA256.HashData(Encoding.UTF8.GetBytes(input)),
            "SHA512" => SHA512.HashData(Encoding.UTF8.GetBytes(input)),
            "MD5" => MD5.HashData(Encoding.UTF8.GetBytes(input)),
            _ => SHA256.HashData(Encoding.UTF8.GetBytes(input))
        };

        return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
    }

    private string NormalizeValueForHash(object value)
    {
        if (value == null || value == DBNull.Value)
            return "NULL";

        return value switch
        {
            DateTime dt => dt.ToString("O"), // ISO 8601
            DateTimeOffset dto => dto.ToString("O"),
            decimal d => d.ToString("G", CultureInfo.InvariantCulture),
            double d => d.ToString("G", CultureInfo.InvariantCulture),
            float f => f.ToString("G", CultureInfo.InvariantCulture),
            bool b => b ? "TRUE" : "FALSE",
            byte[] bytes => Convert.ToBase64String(bytes),
            string s => s.Normalize(NormalizationForm.FormC).TrimStart('\uFEFF'),
            _ => value.ToString()!
        };
    }

    private string CreateCompositeKey(Dictionary<string, object> row, List<string> keyColumns)
    {
        var keyParts = keyColumns
            .Select(col => row.TryGetValue(col, out var val) ? (val?.ToString() ?? "NULL") : "MISSING")
            .ToList();

        return string.Join("|", keyParts);
    }
}

/// <summary>
/// Result of delta sync operation for imports
/// </summary>
public class ImportDeltaSyncResult
{
    public List<Dictionary<string, object>> NewRows { get; set; } = new();
    public List<Dictionary<string, object>> ChangedRows { get; set; } = new();
    public List<Dictionary<string, object>> UnchangedRows { get; set; } = new();
    public int TotalRowsProcessed { get; set; }
    public bool DeltaSyncEnabled { get; set; }
    public List<string> KeyColumns { get; set; } = new();
    public DeltaSyncMode Mode { get; set; }
}

/// <summary>
/// Statistics for import delta sync
/// </summary>
public class ImportDeltaSyncStats
{
    public int TotalTrackedRows { get; set; }
    public DateTime? FirstTrackedAt { get; set; }
    public DateTime? LastTrackedAt { get; set; }
}
