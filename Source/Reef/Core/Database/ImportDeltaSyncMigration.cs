using Microsoft.Data.Sqlite;
using Dapper;
using Serilog;

namespace Reef.Core.Database;

/// <summary>
/// Database migration to add Delta Sync capabilities for Import profiles
/// Creates ImportDeltaSyncState table for tracking delta sync state for imports
/// </summary>
public class ImportDeltaSyncMigration
{
    private readonly string _connectionString;

    public ImportDeltaSyncMigration(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>
    /// Apply all import delta sync migrations
    /// </summary>
    public async Task ApplyAsync()
    {
        Log.Debug("Applying Import Delta Sync database migrations...");

        try
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            // Check if already applied by checking for the table
            var tableExists = await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='ImportDeltaSyncState'");

            if (tableExists > 0)
            {
                Log.Debug("Import Delta Sync migrations already applied");
                return;
            }

            await CreateImportDeltaSyncStateTableAsync(conn);
            await CreateIndexesAsync(conn);

            Log.Information("✓ Import Delta Sync migrations completed successfully");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to apply Import Delta Sync migrations: {ErrorMessage}", ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Create the ImportDeltaSyncState table for storing row hashes
    /// </summary>
    private async Task CreateImportDeltaSyncStateTableAsync(SqliteConnection conn)
    {
        Log.Debug("Creating ImportDeltaSyncState table...");

        await conn.ExecuteAsync(@"
            CREATE TABLE ImportDeltaSyncState (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ImportProfileId INTEGER NOT NULL,
                CompositeKey TEXT NOT NULL,
                RowHash TEXT NOT NULL,
                LastSeenExecutionId INTEGER NOT NULL,
                LastSeenAt TEXT NOT NULL DEFAULT (datetime('now')),

                FOREIGN KEY (ImportProfileId) REFERENCES ImportProfiles(Id) ON DELETE CASCADE,
                FOREIGN KEY (LastSeenExecutionId) REFERENCES ImportExecutions(Id)
            )
        ");

        Log.Debug("✓ ImportDeltaSyncState table created");
    }

    /// <summary>
    /// Create indexes for optimal query performance
    /// </summary>
    private async Task CreateIndexesAsync(SqliteConnection conn)
    {
        Log.Debug("Creating import delta sync indexes...");

        await CreateIndexIfNotExistsAsync(conn, "idx_import_deltasync_profile_key",
            "ImportDeltaSyncState", "(ImportProfileId, CompositeKey)");

        await CreateIndexIfNotExistsAsync(conn, "idx_import_deltasync_profile_hash",
            "ImportDeltaSyncState", "(ImportProfileId, RowHash)");

        await CreateIndexIfNotExistsAsync(conn, "idx_import_deltasync_execution",
            "ImportDeltaSyncState", "(LastSeenExecutionId)");

        Log.Debug("✓ Indexes created");
    }

    /// <summary>
    /// Create an index if it doesn't already exist
    /// </summary>
    private async Task CreateIndexIfNotExistsAsync(SqliteConnection conn, string indexName, string tableName, string columns)
    {
        try
        {
            var indexExists = await conn.ExecuteScalarAsync<long>(
                $"SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='{indexName}'");

            if (indexExists == 0)
            {
                await conn.ExecuteAsync($"CREATE INDEX {indexName} ON {tableName} {columns}");
                Log.Debug("  Created index {IndexName}", indexName);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to create index {IndexName}", indexName);
        }
    }
}
