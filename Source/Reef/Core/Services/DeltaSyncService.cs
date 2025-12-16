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
        Profile profile)
    {
        var config = new DeltaSyncConfig
        {
            Enabled = profile.DeltaSyncEnabled,
            ReefIdColumn = profile.DeltaSyncReefIdColumn!,
            HashAlgorithm = profile.DeltaSyncHashAlgorithm ?? "SHA256",
            IncludeDeleted = profile.DeltaSyncTrackDeletes,
            DuplicateStrategy = profile.DeltaSyncDuplicateStrategy ?? "Strict",
            NullStrategy = profile.DeltaSyncNullStrategy ?? "Strict",
            NumericPrecision = profile.DeltaSyncNumericPrecision ?? 6,
            RemoveNonPrintable = profile.DeltaSyncRemoveNonPrintable,
            ReefIdNormalization = profile.DeltaSyncReefIdNormalization ?? "Trim"
        };

        // Validate and clean rows
        rows = await ValidateAndCleanRowsAsync(rows, config, profileId);

        // Get previous hash state
        var previousHashes = await GetPreviousHashStateAsync(profileId);

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
            await DetectSchemaChangeAsync(profileId, currentSchema, profile.DeltaSyncResetOnSchemaChange);
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
        DeltaSyncResult deltaSyncResult)
    {
        Log.Information("Delta sync: Committing state for profile {ProfileId}, execution {ExecutionId}, StateCount={StateCount}", 
            profileId, executionId, deltaSyncResult.NewHashState.Count);

        // Update hash state for all current rows
        await UpdateHashStateAsync(profileId, executionId, deltaSyncResult.NewHashState);

        // Mark deleted rows
        if (deltaSyncResult.DeletedReefIds.Any())
        {
            await MarkDeletedRowsAsync(profileId, executionId, deltaSyncResult.DeletedReefIds);
        }

        Log.Debug("Delta sync: State committed successfully for profile {ProfileId}", profileId);
    }

    /// <summary>
    /// Get previous hash state for a profile
    /// </summary>
    public async Task<Dictionary<string, string>> GetPreviousHashStateAsync(int profileId)
    {
        using var db = new SqliteConnection(_connectionString);
        
        var rows = await db.QueryAsync<(string ReefId, string RowHash)>(
            @"SELECT ReefId, RowHash 
              FROM DeltaSyncState 
              WHERE ProfileId = @ProfileId AND IsDeleted = 0",
            new { ProfileId = profileId });

        return rows.ToDictionary(r => r.ReefId, r => r.RowHash);
    }

    /// <summary>
    /// Update hash state after execution
    /// </summary>
    public async Task UpdateHashStateAsync(
        int profileId,
        int executionId,
        Dictionary<string, string> newHashState)
    {
        using var db = new SqliteConnection(_connectionString);
        await db.OpenAsync();
        
        Log.Information("UpdateHashStateAsync: Saving {Count} hash entries for profile {ProfileId}, execution {ExecutionId}",
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
                          WHERE ProfileId = @ProfileId AND ReefId = @ReefId",
                        new { ProfileId = profileId, ReefId = reefId }, transaction);

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
                            new { Id = existingId.Value, Hash = hash, ExecutionId = executionId },
                            transaction);
                    }
                    else
                    {
                        // Insert new row
                        await db.ExecuteAsync(
                            @"INSERT INTO DeltaSyncState 
                              (ProfileId, ReefId, RowHash, LastSeenExecutionId, FirstSeenAt, LastSeenAt)
                              VALUES (@ProfileId, @ReefId, @Hash, @ExecutionId, datetime('now'), datetime('now'))",
                            new { ProfileId = profileId, ReefId = reefId, Hash = hash, ExecutionId = executionId },
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
        IEnumerable<string> deletedReefIds)
    {
        using var db = new SqliteConnection(_connectionString);
        
        foreach (var reefId in deletedReefIds)
        {
            await db.ExecuteAsync(
                @"UPDATE DeltaSyncState 
                  SET IsDeleted = 1, 
                      DeletedAt = datetime('now'),
                      LastSeenExecutionId = @ExecutionId
                  WHERE ProfileId = @ProfileId AND ReefId = @ReefId",
                new { ProfileId = profileId, ReefId = reefId, ExecutionId = executionId });
        }
    }

    /// <summary>
    /// Reset delta sync state for a profile (manual reset)
    /// </summary>
    public async Task ResetDeltaSyncStateAsync(int profileId)
    {
        using var db = new SqliteConnection(_connectionString);
        
        await db.ExecuteAsync(
            "DELETE FROM DeltaSyncState WHERE ProfileId = @ProfileId",
            new { ProfileId = profileId });

        Log.Information("Delta sync state reset for profile {ProfileId}", profileId);
    }

    /// <summary>
    /// Reset delta sync state for specific rows
    /// </summary>
    public async Task<int> ResetDeltaSyncRowsAsync(int profileId, List<string> reefIds)
    {
        using var db = new SqliteConnection(_connectionString);
        
        var deleted = await db.ExecuteAsync(
            "DELETE FROM DeltaSyncState WHERE ProfileId = @ProfileId AND ReefId IN @ReefIds",
            new { ProfileId = profileId, ReefIds = reefIds });

        Log.Information("Reset delta sync state for {Count} rows in profile {ProfileId}", 
            deleted, profileId);
        
        return deleted;
    }

    /// <summary>
    /// Reset delta sync state by criteria
    /// </summary>
    public async Task<int> ResetDeltaSyncByCriteriaAsync(
        int profileId, 
        DeltaSyncResetCriteria criteria)
    {
        using var db = new SqliteConnection(_connectionString);
        
        var sql = "DELETE FROM DeltaSyncState WHERE ProfileId = @ProfileId";
        var parameters = new DynamicParameters();
        parameters.Add("ProfileId", profileId);

        if (criteria.LastSeenBefore.HasValue)
        {
            sql += " AND LastSeenAt < @LastSeenBefore";
            parameters.Add("LastSeenBefore", criteria.LastSeenBefore.Value.ToString("yyyy-MM-dd HH:mm:ss"));
        }

        if (criteria.IsDeleted.HasValue)
        {
            sql += " AND IsDeleted = @IsDeleted";
            parameters.Add("IsDeleted", criteria.IsDeleted.Value ? 1 : 0);
        }

        if (!string.IsNullOrWhiteSpace(criteria.ReefIdPattern))
        {
            sql += " AND ReefId LIKE @Pattern";
            parameters.Add("Pattern", criteria.ReefIdPattern.Replace("*", "%"));
        }

        var deleted = await db.ExecuteAsync(sql, parameters);
        
        Log.Information("Reset delta sync state for {Count} rows matching criteria in profile {ProfileId}",
            deleted, profileId);

        return deleted;
    }

    /// <summary>
    /// Generate hash baseline for all current rows without marking them as changes
    /// This establishes the baseline so future executions will only detect new/updated rows
    /// </summary>
    public async Task<int> GenerateHashBaselineAsync(int profileId, List<Dictionary<string, object>> queryResults)
    {
        using var db = new SqliteConnection(_connectionString);

        // Get the profile to access configuration
        var profile = await db.QueryFirstOrDefaultAsync<Profile>(
            "SELECT * FROM Profiles WHERE Id = @Id",
            new { Id = profileId });

        if (profile == null)
        {
            throw new InvalidOperationException($"Profile {profileId} not found");
        }

        // Validate required fields
        if (string.IsNullOrEmpty(profile.DeltaSyncReefIdColumn))
        {
            throw new InvalidOperationException("ReefId column is required for delta sync");
        }

        // Clear existing state for this profile to establish fresh baseline
        await db.ExecuteAsync(
            "DELETE FROM DeltaSyncState WHERE ProfileId = @ProfileId",
            new { ProfileId = profileId });

        Log.Information("Starting hash baseline generation for profile {ProfileId}. Processing {RowCount} rows",
            profileId, queryResults.Count);

        // Use the same ProcessDeltaAsync logic that works perfectly during normal execution
        // This calculates hashes using the exact same algorithm and validation
        var deltaSyncResult = await ProcessDeltaAsync(profileId, queryResults, profile);

        // Use ExecutionId = 0 to mark these as baseline hashes (synthetic execution)
        // We disable foreign key constraints temporarily since baseline execution record may not exist
        int baselineExecutionId = 0;

        try
        {
            // Temporarily disable foreign key constraints for baseline hash insertion
            // This allows us to insert baseline hashes without a real execution record
            await db.ExecuteAsync("PRAGMA foreign_keys = OFF");

            try
            {
                // Persist the calculated hashes using the same method as normal execution
                await CommitDeltaSyncAsync(profileId, baselineExecutionId, deltaSyncResult);
            }
            finally
            {
                // Always re-enable foreign key constraints
                await db.ExecuteAsync("PRAGMA foreign_keys = ON");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error persisting baseline hashes for profile {ProfileId}", profileId);
            throw;
        }

        var totalProcessed = deltaSyncResult.TotalRowsProcessed;
        Log.Information("Generated hash baseline for {Count} rows in profile {ProfileId}", totalProcessed, profileId);

        return totalProcessed;
    }

    /// <summary>
    /// Clean up old hash state based on retention policy
    /// </summary>
    public async Task CleanupOldStateAsync(int profileId, int retentionDays)
    {
        using var db = new SqliteConnection(_connectionString);
        
        var cutoffDate = DateTime.UtcNow.AddDays(-retentionDays);
        
        var deleted = await db.ExecuteAsync(
            @"DELETE FROM DeltaSyncState 
              WHERE ProfileId = @ProfileId 
              AND LastSeenAt < @CutoffDate
              AND IsDeleted = 1",
            new { ProfileId = profileId, CutoffDate = cutoffDate.ToString("yyyy-MM-dd HH:mm:ss") });

        if (deleted > 0)
        {
            Log.Information("Cleaned up {Count} old delta sync records for profile {ProfileId}", 
                deleted, profileId);
        }
    }

    /// <summary>
    /// Get global delta sync statistics
    /// </summary>
    public async Task<DeltaSyncStats> GetGlobalStatsAsync()
    {
        using var db = new SqliteConnection(_connectionString);
        
        var stats = await db.QueryFirstOrDefaultAsync<DeltaSyncStats>(@"
            SELECT 
                (SELECT COUNT(DISTINCT ProfileId) FROM DeltaSyncState) AS ProfilesWithDeltaSync,
                (SELECT COUNT(*) FROM DeltaSyncState WHERE IsDeleted = 0) AS TotalHashedRows,
                (SELECT COUNT(*) FROM DeltaSyncState WHERE IsDeleted = 1) AS TotalDeletedRows
        ");

        return stats ?? new DeltaSyncStats();
    }

    /// <summary>
    /// Get delta sync statistics for a specific profile
    /// </summary>
    public async Task<ProfileDeltaSyncStats> GetProfileStatsAsync(int profileId)
    {
        using var db = new SqliteConnection(_connectionString);
        
        var stats = await db.QueryFirstOrDefaultAsync<ProfileDeltaSyncStats>(@"
            SELECT 
                COUNT(CASE WHEN IsDeleted = 0 THEN 1 END) AS ActiveRows,
                COUNT(CASE WHEN IsDeleted = 1 THEN 1 END) AS DeletedRows,
                COUNT(*) AS TotalTrackedRows,
                MIN(FirstSeenAt) AS FirstTrackedAt,
                MAX(LastSeenAt) AS LastTrackedAt
            FROM DeltaSyncState
            WHERE ProfileId = @ProfileId
        ", new { ProfileId = profileId });

        return stats ?? new ProfileDeltaSyncStats();
    }

    // ===== Private Helper Methods =====

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

    private async Task DetectSchemaChangeAsync(int profileId, List<string> currentSchema, bool resetOnChange)
    {
        using var db = new SqliteConnection(_connectionString);
        
        // Get previous schema if exists
        var previousSchemaJson = await db.ExecuteScalarAsync<string>(
            "SELECT LastSeenAt FROM DeltaSyncState WHERE ProfileId = @ProfileId LIMIT 1",
            new { ProfileId = profileId });

        if (previousSchemaJson != null)
        {
            // For now, we'll implement basic schema change detection
            // In a full implementation, you'd store and compare the actual schema
            Log.Debug("Schema validation for profile {ProfileId} - continuing", profileId);
        }
    }
}
