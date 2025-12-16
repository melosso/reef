using Microsoft.Data.Sqlite;
using Dapper;
using Serilog;

namespace Reef.Core.Database;

// To-do: Remove this after migration to DatabaseInitializer.cs

/// <summary>
/// Database migration to add Delta Sync capabilities to Reef
/// Creates DeltaSyncState table and adds delta sync configuration columns to Profile table
/// Run this on application startup after other migrations
/// </summary>
public class DeltaSyncMigration
{
    private readonly string _connectionString;

    /// <summary>
    /// Initializes a new instance of the DeltaSyncMigration class
    /// </summary>
    /// <param name="connectionString">SQLite connection string</param>
    public DeltaSyncMigration(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>
    /// Apply all delta sync migrations
    /// </summary>
    public async Task ApplyAsync()
    {
        Log.Debug("Applying Delta Sync database migrations...");

        try
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            // Check if already applied by checking for a specific column in Profiles table
            var columnExists = await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM pragma_table_info('Profiles') WHERE name='DeltaSyncEnabled'");

            if (columnExists > 0)
            {
                Log.Debug("Delta Sync migrations already applied");
                return;
            }

            await CreateDeltaSyncStateTableAsync(conn);
            await AddProfileColumnsAsync(conn);
            await AddProfileExecutionColumnsAsync(conn);
            await CreateIndexesAsync(conn);

            Log.Information("✓ Delta Sync migrations completed successfully");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to apply Delta Sync migrations: {ErrorMessage}", ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Create the DeltaSyncState table for storing row hashes
    /// </summary>
    private async Task CreateDeltaSyncStateTableAsync(SqliteConnection conn)
    {
        Log.Debug("Creating DeltaSyncState table...");

        // Check if table already exists
        var tableExists = await conn.ExecuteScalarAsync<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='DeltaSyncState'");

        if (tableExists > 0)
        {
            Log.Debug("✓ DeltaSyncState table already exists");
            return;
        }

        await conn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS DeltaSyncState (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ProfileId INTEGER NOT NULL,
                ReefId TEXT NOT NULL,
                RowHash TEXT NOT NULL,
                LastSeenExecutionId INTEGER NOT NULL,
                FirstSeenAt TEXT NOT NULL DEFAULT (datetime('now')),
                LastSeenAt TEXT NOT NULL DEFAULT (datetime('now')),
                IsDeleted INTEGER NOT NULL DEFAULT 0,
                DeletedAt TEXT NULL,

                FOREIGN KEY (ProfileId) REFERENCES Profiles(Id) ON DELETE CASCADE,
                FOREIGN KEY (LastSeenExecutionId) REFERENCES ProfileExecution(Id)
            )
        ");

        Log.Debug("✓ DeltaSyncState table created");
    }

    /// <summary>
    /// Add delta sync configuration columns to Profile table
    /// </summary>
    private async Task AddProfileColumnsAsync(SqliteConnection conn)
    {
        Log.Debug("Adding delta sync columns to Profiles table...");

        // Basic configuration columns
        await AddColumnIfNotExistsAsync(conn, "Profiles", "DeltaSyncEnabled", "INTEGER NOT NULL DEFAULT 0");
        await AddColumnIfNotExistsAsync(conn, "Profiles", "DeltaSyncReefIdColumn", "TEXT NULL");
        await AddColumnIfNotExistsAsync(conn, "Profiles", "DeltaSyncHashAlgorithm", "TEXT NULL DEFAULT 'SHA256'");
        await AddColumnIfNotExistsAsync(conn, "Profiles", "DeltaSyncTrackDeletes", "INTEGER NOT NULL DEFAULT 0");
        await AddColumnIfNotExistsAsync(conn, "Profiles", "DeltaSyncRetentionDays", "INTEGER NULL");

        // Edge case handling configuration columns
        await AddColumnIfNotExistsAsync(conn, "Profiles", "DeltaSyncDuplicateStrategy", "TEXT NULL DEFAULT 'Strict'");
        await AddColumnIfNotExistsAsync(conn, "Profiles", "DeltaSyncNullStrategy", "TEXT NULL DEFAULT 'Strict'");
        await AddColumnIfNotExistsAsync(conn, "Profiles", "DeltaSyncResetOnSchemaChange", "INTEGER NOT NULL DEFAULT 0");
        await AddColumnIfNotExistsAsync(conn, "Profiles", "DeltaSyncNumericPrecision", "INTEGER NOT NULL DEFAULT 6");
        await AddColumnIfNotExistsAsync(conn, "Profiles", "DeltaSyncRemoveNonPrintable", "INTEGER NOT NULL DEFAULT 0");
        await AddColumnIfNotExistsAsync(conn, "Profiles", "DeltaSyncReefIdNormalization", "TEXT NULL DEFAULT 'Trim'");

        Log.Debug("✓ Profiles table columns added");
    }

    /// <summary>
    /// Add delta sync metrics columns to ProfileExecution table
    /// </summary>
    private async Task AddProfileExecutionColumnsAsync(SqliteConnection conn)
    {
        Log.Debug("Adding delta sync metrics columns to ProfileExecution table...");

        await AddColumnIfNotExistsAsync(conn, "ProfileExecution", "DeltaSyncNewRows", "INTEGER NULL");
        await AddColumnIfNotExistsAsync(conn, "ProfileExecution", "DeltaSyncChangedRows", "INTEGER NULL");
        await AddColumnIfNotExistsAsync(conn, "ProfileExecution", "DeltaSyncDeletedRows", "INTEGER NULL");
        await AddColumnIfNotExistsAsync(conn, "ProfileExecution", "DeltaSyncUnchangedRows", "INTEGER NULL");
        await AddColumnIfNotExistsAsync(conn, "ProfileExecution", "DeltaSyncTotalHashedRows", "INTEGER NULL");

        Log.Debug("✓ ProfileExecution table columns added");
    }

    /// <summary>
    /// Create indexes for optimal query performance
    /// </summary>
    private async Task CreateIndexesAsync(SqliteConnection conn)
    {
        Log.Debug("Creating delta sync indexes...");

        await CreateIndexIfNotExistsAsync(conn, "idx_deltasync_profile_reefid",
            "DeltaSyncState", "(ProfileId, ReefId)");

        await CreateIndexIfNotExistsAsync(conn, "idx_deltasync_profile_hash",
            "DeltaSyncState", "(ProfileId, RowHash)");

        await CreateIndexIfNotExistsAsync(conn, "idx_deltasync_execution",
            "DeltaSyncState", "(LastSeenExecutionId)");

        await CreateIndexIfNotExistsAsync(conn, "idx_deltasync_deleted",
            "DeltaSyncState", "(ProfileId, IsDeleted)");

        Log.Debug("✓ Indexes created");
    }

    /// <summary>
    /// Add a column to a table if it doesn't already exist
    /// </summary>
    private async Task AddColumnIfNotExistsAsync(SqliteConnection conn, string tableName, string columnName, string columnDefinition)
    {
        try
        {
            var columnExists = await conn.ExecuteScalarAsync<long>(
                $"SELECT COUNT(*) FROM pragma_table_info('{tableName}') WHERE name='{columnName}'");

            if (columnExists == 0)
            {
                await conn.ExecuteAsync($"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnDefinition}");
                Log.Debug("  Added column {Table}.{Column}", tableName, columnName);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to add column {Table}.{Column}", tableName, columnName);
        }
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
