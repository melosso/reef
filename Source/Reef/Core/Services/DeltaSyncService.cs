using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;
using Reef.Core.Models;
using Serilog;

namespace Reef.Core.Services;

/// <summary>
/// Service for managing delta sync operations
/// Handles hash calculation, comparison, and state management
/// </summary>
public class DeltaSyncService
{
    private readonly string _connectionString;

    private static int? Owner(int profileId, string? scope) => scope is null ? profileId : null;

    public DeltaSyncService(DatabaseConfig config)
    {
        _connectionString = config.ConnectionString;
    }

    /// <summary>
    /// Calculate hash for a single row
    /// </summary>
    public async Task<string> CalculateRowHashAsync(
        Dictionary<string, object> row,
        string reefIdColumn,
        DeltaSyncConfig config)
    {
        return await Task.Run(() =>
        {
            // Include ReefId in hash to prevent collisions
            var reefId = NormalizeReefId(row[reefIdColumn], config.ReefIdNormalization);
            var sb = new StringBuilder();
            sb.Append($"REEFID:{reefId}|");

            // Sort keys for consistent ordering
            var sortedKeys = row.Keys.OrderBy(k => k).ToList();

            foreach (var key in sortedKeys)
            {
                var value = NormalizeValueForHash(row[key], config);
                sb.Append($"{key}={value};");
            }

            // Calculate hash
            var hashInput = sb.ToString();
            return CalculateHash(hashInput, config.HashAlgorithm);
        });
    }

    /// <summary>
    /// Process query results and determine deltas
    /// </summary>
    public async Task<DeltaSyncResult> ProcessDeltaAsync(
        int profileId,
        List<Dictionary<string, object>> rows,
        DeltaSyncConfig config,
        string? scope = null)
    {
        // Validate and clean rows
        rows = await ValidateAndCleanRowsAsync(rows, config, profileId);

        // Get previous hash state
        var previousHashes = await GetPreviousHashStateAsync(profileId, scope);

        // Calculate current hashes
        var currentHashes = new Dictionary<string, string>();
        var result = new DeltaSyncResult
        {
            NewRows = new List<Dictionary<string, object>>(),
            ChangedRows = new List<Dictionary<string, object>>(),
            UnchangedRows = new List<Dictionary<string, object>>(),
            DeletedReefIds = new List<string>(),
            NewHashState = new Dictionary<string, string>(),
            TotalRowsProcessed = rows.Count
        };

        // Detect schema changes
        if (rows.Any())
        {
            var currentSchema = rows.First().Keys.OrderBy(k => k).ToList();
            await DetectSchemaChangeAsync(profileId, currentSchema, config.ResetOnSchemaChange, scope);
        }

        // Process each row
        foreach (var row in rows)
        {
            var reefId = NormalizeReefId(row[config.ReefIdColumn], config.ReefIdNormalization);
            var hash = await CalculateRowHashAsync(row, config.ReefIdColumn, config);

            currentHashes[reefId] = hash;
            result.NewHashState[reefId] = hash;

            bool foundInPrevious = previousHashes.ContainsKey(reefId);
            string? previousHash = foundInPrevious ? previousHashes[reefId] : null;

            Log.Information("Delta sync row: ReefId='{ReefId}', CurrentHash='{CurrentHash}', FoundInPrevious={Found}, PreviousHash='{PreviousHash}'",
                reefId, hash?.Substring(0, 8) + "...", foundInPrevious, previousHash?.Substring(0, 8) + "...");

            if (!foundInPrevious)
            {
                // New row
                result.NewRows.Add(row);
            }
            else if (previousHashes[reefId] != hash)
            {
                // Changed row
                result.ChangedRows.Add(row);
            }
            else
            {
                // Unchanged row
                result.UnchangedRows.Add(row);
            }
        }

        // Find deleted rows (in previous state but not in current)
        result.DeletedReefIds = previousHashes.Keys
            .Except(currentHashes.Keys)
            .ToList();

        // IMPORTANT: Do NOT save hash state yet! 
        // Hashes should only be committed AFTER successful transformation and destination write
        // This prevents marking rows as exported when processing fails
        // The ExecutionService will call CommitDeltaSyncAsync() after success

        Log.Debug(
            "Delta sync for profile {ProfileId} prepared: {New} new, {Changed} changed, {Deleted} deleted, {Unchanged} unchanged (hashes NOT committed yet)",
            profileId, result.NewRows.Count, result.ChangedRows.Count,
            result.DeletedReefIds.Count, result.UnchangedRows.Count);

        return result;
    }

    /// <summary>
    /// Commit delta sync hash state after successful execution
    /// This should ONLY be called AFTER transformation and destination write have completed successfully
    /// </summary>
    public async Task CommitDeltaSyncAsync(
        int profileId,
        int executionId,
        DeltaSyncResult deltaSyncResult,
        string? scope = null)
    {
        Log.Debug("Delta sync: Committing state for profile {ProfileId}, execution {ExecutionId}, StateCount={StateCount}",
            profileId, executionId, deltaSyncResult.NewHashState.Count);

        // Update hash state for all current rows
        await UpdateHashStateAsync(profileId, executionId, deltaSyncResult.NewHashState, scope);

        // Mark deleted rows
        if (deltaSyncResult.DeletedReefIds.Any())
        {
            await MarkDeletedRowsAsync(profileId, executionId, deltaSyncResult.DeletedReefIds, scope);
        }

        Log.Debug("Delta sync: State committed successfully for profile {ProfileId}", profileId);
    }

    /// <summary>
    /// Get previous hash state for a profile
    /// </summary>
    public async Task<Dictionary<string, string>> GetPreviousHashStateAsync(int profileId, string? scope = null)
    {
        using var db = new SqliteConnection(_connectionString);

        var rows = await db.QueryAsync<(string ReefId, string RowHash)>(
            @"SELECT ReefId, RowHash 
              FROM DeltaSyncState 
              WHERE ProfileId IS @ProfileId AND Scope IS @Scope AND IsDeleted = 0",
            new { ProfileId = Owner(profileId, scope), Scope = scope });

        return rows.ToDictionary(r => r.ReefId, r => r.RowHash);
    }

    /// <summary>
    /// Update hash state after execution
    /// </summary>
    public async Task UpdateHashStateAsync(
        int profileId,
        int executionId,
        Dictionary<string, string> newHashState,
        string? scope = null)
    {
        using var db = new SqliteConnection(_connectionString);
        await db.OpenAsync();

        Log.Debug("UpdateHashStateAsync: Saving {Count} hash entries for profile {ProfileId}, execution {ExecutionId}",
            newHashState.Count, profileId, executionId);

        using var transaction = await db.BeginTransactionAsync();
        try
        {
            // Process in batches for large datasets
            const int batchSize = 1000;
            var entries = newHashState.ToList();

            for (int i = 0; i < entries.Count; i += batchSize)
            {
                var batch = entries.Skip(i).Take(batchSize).ToList();

                foreach (var (reefId, hash) in batch)
                {
                    // Check if row exists
                    var existingId = await db.ExecuteScalarAsync<long?>(
                        @"SELECT Id FROM DeltaSyncState 
                          WHERE ProfileId IS @ProfileId AND Scope IS @Scope AND ReefId = @ReefId",
                        new { ProfileId = Owner(profileId, scope), Scope = scope, ReefId = reefId }, transaction);

                    Log.Debug("UpdateHashStateAsync: ReefId='{ReefId}', Hash='{Hash}', Exists={Exists}",
                        reefId, hash?.Substring(0, 8) + "...", existingId.HasValue);

                    if (existingId.HasValue)
                    {
                        // Update existing row
                        await db.ExecuteAsync(
                            @"UPDATE DeltaSyncState 
                              SET RowHash = @Hash, 
                                  LastSeenExecutionId = @ExecutionId,
                                  LastSeenAt = datetime('now'),
                                  IsDeleted = 0,
                                  DeletedAt = NULL
                              WHERE Id = @Id",
                            new { Id = existingId.Value, Hash = hash, ExecutionId = scope is null ? executionId : (int?)null },
                            transaction);
                    }
                    else
                    {
                        // Insert new row
                        await db.ExecuteAsync(
                            @"INSERT INTO DeltaSyncState 
                              (ProfileId, Scope, ReefId, RowHash, LastSeenExecutionId, FirstSeenAt, LastSeenAt)
                              VALUES (@ProfileId, @Scope, @ReefId, @Hash, @ExecutionId, datetime('now'), datetime('now'))",
                            new { ProfileId = Owner(profileId, scope), Scope = scope, ReefId = reefId, Hash = hash, ExecutionId = scope is null ? executionId : (int?)null },
                            transaction);
                    }
                }
            }

            await transaction.CommitAsync();
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            Log.Error(ex, "Failed to update hash state for profile {ProfileId}: {ErrorMessage}", profileId, ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Mark rows as deleted
    /// </summary>
    public async Task MarkDeletedRowsAsync(
        int profileId,
        int executionId,
        IEnumerable<string> deletedReefIds,
        string? scope = null)
    {
        using var db = new SqliteConnection(_connectionString);

        foreach (var reefId in deletedReefIds)
        {
            await db.ExecuteAsync(
                @"UPDATE DeltaSyncState 
                  SET IsDeleted = 1, 
                      DeletedAt = datetime('now'),
                      LastSeenExecutionId = @ExecutionId
                  WHERE ProfileId IS @ProfileId AND Scope IS @Scope AND ReefId = @ReefId",
                new { ProfileId = Owner(profileId, scope), Scope = scope, ReefId = reefId, ExecutionId = scope is null ? executionId : (int?)null });
        }
    }





    /// <summary>
    /// Clean up old hash state based on retention policy
    /// </summary>
    public async Task CleanupOldStateAsync(int profileId, int retentionDays, string? scope = null)
    {
        using var db = new SqliteConnection(_connectionString);

        var cutoffDate = DateTime.UtcNow.AddDays(-retentionDays);

        var deleted = await db.ExecuteAsync(
            @"DELETE FROM DeltaSyncState 
              WHERE ProfileId IS @ProfileId AND Scope IS @Scope 
              AND LastSeenAt < @CutoffDate
              AND IsDeleted = 1",
            new { ProfileId = Owner(profileId, scope), Scope = scope, CutoffDate = cutoffDate.ToString("yyyy-MM-dd HH:mm:ss") });

        if (deleted > 0)
        {
            Log.Information("Cleaned up {Count} old delta sync records for profile {ProfileId}",
                deleted, profileId);
        }
    }



    /// <summary>
    /// Normalize a row's column keys to handle case-insensitive matching
    /// Ensures the ReefId column can be found regardless of case
    /// </summary>
    private Dictionary<string, object> NormalizeRowColumnKeys(Dictionary<string, object> row, string reefIdColumn)
    {
        // Find the actual column key that matches reefIdColumn (case-insensitive)
        var actualReefIdKey = row.Keys.FirstOrDefault(k => k.Equals(reefIdColumn, StringComparison.OrdinalIgnoreCase));

        if (actualReefIdKey == null || actualReefIdKey == reefIdColumn)
        {
            // Column already exists with exact case or doesn't exist
            return row;
        }

        // Create a new dictionary with the column renamed to the expected case
        var normalizedRow = new Dictionary<string, object>();
        foreach (var kvp in row)
        {
            if (kvp.Key.Equals(reefIdColumn, StringComparison.OrdinalIgnoreCase))
            {
                normalizedRow[reefIdColumn] = kvp.Value;
            }
            else
            {
                normalizedRow[kvp.Key] = kvp.Value;
            }
        }

        return normalizedRow;
    }

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

    private string NormalizeReefId(object reefIdValue, string normalization)
    {
        if (reefIdValue == null || reefIdValue == DBNull.Value)
            return null!;

        var strValue = reefIdValue.ToString()!;

        if (normalization.Contains("Trim"))
            strValue = strValue.Trim();

        if (normalization.Contains("Lowercase"))
            strValue = strValue.ToLowerInvariant();

        if (normalization.Contains("RemoveWhitespace"))
            strValue = Regex.Replace(strValue, @"\s+", "");

        return strValue;
    }

    private string NormalizeValueForHash(object value, DeltaSyncConfig config)
    {
        if (value == null || value == DBNull.Value)
            return "NULL";

        return value switch
        {
            DateTime dt => dt.ToString("O"), // ISO 8601
            DateTimeOffset dto => dto.ToString("O"),
            decimal d => Math.Round(d, config.NumericPrecision).ToString($"F{config.NumericPrecision}", CultureInfo.InvariantCulture),
            double d => Math.Round(d, config.NumericPrecision).ToString($"F{config.NumericPrecision}", CultureInfo.InvariantCulture),
            float f => Math.Round(f, config.NumericPrecision).ToString($"F{config.NumericPrecision}", CultureInfo.InvariantCulture),
            bool b => b ? "TRUE" : "FALSE",
            byte[] bytes => Convert.ToBase64String(bytes),
            string s => NormalizeString(s, config.RemoveNonPrintable),
            _ => value.ToString()!
        };
    }

    private string NormalizeString(string input, bool removeNonPrintable)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        // Normalize to Unicode Form C (canonical composition)
        input = input.Normalize(NormalizationForm.FormC);

        // Remove BOM if present
        input = input.TrimStart('\uFEFF');

        // Optionally remove non-printable characters
        if (removeNonPrintable)
        {
            input = Regex.Replace(input, @"[\p{C}]+", "");
        }

        return input;
    }

    private Task<List<Dictionary<string, object>>> ValidateAndCleanRowsAsync(
        List<Dictionary<string, object>> rows,
        DeltaSyncConfig config,
        int profileId)
    {
        if (!rows.Any())
            return Task.FromResult(rows);

        // Normalize column keys to handle case-insensitivity
        rows = rows.Select(row => NormalizeRowColumnKeys(row, config.ReefIdColumn)).ToList();

        // Check for duplicate ReefIds
        var reefIds = rows.Select(r => NormalizeReefId(r[config.ReefIdColumn], config.ReefIdNormalization)).ToList();
        var duplicates = reefIds.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key).ToList();

        if (duplicates.Any())
        {
            if (config.DuplicateStrategy == "Strict")
            {
                var error = $"Delta sync failed: Duplicate ReefId values found: {string.Join(", ", duplicates.Take(5))}";
                Log.Error("Duplicate ReefId values in profile {ProfileId}: {Duplicates}",
                    profileId, string.Join(", ", duplicates));
                throw new InvalidOperationException(error);
            }
            else if (config.DuplicateStrategy == "Skip")
            {
                Log.Warning("Skipping duplicate ReefId values in profile {ProfileId}", profileId);
                // Keep only first occurrence
                var seen = new HashSet<string>();
                rows = rows.Where(r =>
                {
                    var reefId = NormalizeReefId(r[config.ReefIdColumn], config.ReefIdNormalization);
                    return seen.Add(reefId);
                }).ToList();
            }
        }

        // Check for NULL ReefIds
        var rowsWithNullReefId = rows
            .Where(r => r[config.ReefIdColumn] == null || r[config.ReefIdColumn] == DBNull.Value)
            .ToList();

        if (rowsWithNullReefId.Any())
        {
            if (config.NullStrategy == "Strict")
            {
                var error = $"Delta sync failed: {rowsWithNullReefId.Count} rows have NULL ReefId values";
                Log.Error("NULL ReefId values found in profile {ProfileId}, count: {Count}",
                    profileId, rowsWithNullReefId.Count);
                throw new InvalidOperationException(error);
            }
            else if (config.NullStrategy == "Skip")
            {
                Log.Warning("Skipping {Count} rows with NULL ReefId in profile {ProfileId}",
                    rowsWithNullReefId.Count, profileId);
                rows = rows.Where(r => r[config.ReefIdColumn] != null &&
                                       r[config.ReefIdColumn] != DBNull.Value).ToList();
            }
            else if (config.NullStrategy == "Generate")
            {
                Log.Warning("Generating synthetic ReefIds for {Count} NULL values in profile {ProfileId}",
                    rowsWithNullReefId.Count, profileId);
                foreach (var row in rowsWithNullReefId)
                {
                    row[config.ReefIdColumn] = $"GENERATED_{Guid.NewGuid():N}";
                }
            }
        }

        return Task.FromResult(rows);
    }

    private async Task DetectSchemaChangeAsync(int profileId, List<string> currentSchema, bool resetOnChange, string? scope)
    {
        using var db = new SqliteConnection(_connectionString);

        // Get previous schema if exists
        var previousSchemaJson = await db.ExecuteScalarAsync<string>(
            "SELECT LastSeenAt FROM DeltaSyncState WHERE ProfileId IS @ProfileId AND Scope IS @Scope LIMIT 1",
            new { ProfileId = Owner(profileId, scope), Scope = scope });

        if (previousSchemaJson != null)
        {
            // For now, we'll implement basic schema change detection
            // In a full implementation, you'd store and compare the actual schema
            Log.Debug("Schema validation for profile {ProfileId} - continuing", profileId);
        }
    }
}
