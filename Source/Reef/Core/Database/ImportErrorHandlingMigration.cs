using Microsoft.Data.Sqlite;
using Dapper;
using Serilog;

namespace Reef.Core.Database;

/// <summary>
/// Database migration to add Error Handling and Quarantine capabilities for Import
/// Creates ImportErrorLog and ImportQuarantine tables
/// </summary>
public class ImportErrorHandlingMigration
{
    private readonly string _connectionString;

    public ImportErrorHandlingMigration(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>
    /// Apply all import error handling migrations
    /// </summary>
    public async Task ApplyAsync()
    {
        Log.Debug("Applying Import Error Handling database migrations...");

        try
        {
            using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync();

            // Check if already applied
            var tableExists = await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='ImportErrorLog'");

            if (tableExists > 0)
            {
                Log.Debug("Import Error Handling migrations already applied");
                return;
            }

            await CreateErrorLogTableAsync(conn);
            await CreateQuarantineTableAsync(conn);
            await CreateIndexesAsync(conn);

            Log.Information("✓ Import Error Handling migrations completed successfully");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to apply Import Error Handling migrations: {ErrorMessage}", ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Create the ImportErrorLog table for storing error details
    /// </summary>
    private async Task CreateErrorLogTableAsync(SqliteConnection conn)
    {
        Log.Debug("Creating ImportErrorLog table...");

        await conn.ExecuteAsync(@"
            CREATE TABLE ImportErrorLog (
                Id TEXT PRIMARY KEY,
                ImportProfileId INTEGER NOT NULL,
                ExecutionId INTEGER NOT NULL,
                ErrorMessage TEXT NOT NULL,
                RowDataSample TEXT,
                RowDataJson TEXT,
                CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),

                FOREIGN KEY (ImportProfileId) REFERENCES ImportProfiles(Id) ON DELETE CASCADE,
                FOREIGN KEY (ExecutionId) REFERENCES ImportExecutions(Id) ON DELETE CASCADE
            )
        ");

        Log.Debug("✓ ImportErrorLog table created");
    }

    /// <summary>
    /// Create the ImportQuarantine table for storing quarantined rows
    /// </summary>
    private async Task CreateQuarantineTableAsync(SqliteConnection conn)
    {
        Log.Debug("Creating ImportQuarantine table...");

        await conn.ExecuteAsync(@"
            CREATE TABLE ImportQuarantine (
                Id TEXT PRIMARY KEY,
                ImportProfileId INTEGER NOT NULL,
                ExecutionId INTEGER NOT NULL,
                RowData TEXT NOT NULL,
                ErrorMessage TEXT NOT NULL,
                ErrorId TEXT,
                ReviewedAt TEXT NULL,
                ReviewAction TEXT NULL,
                CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),

                FOREIGN KEY (ImportProfileId) REFERENCES ImportProfiles(Id) ON DELETE CASCADE,
                FOREIGN KEY (ExecutionId) REFERENCES ImportExecutions(Id) ON DELETE CASCADE
            )
        ");

        Log.Debug("✓ ImportQuarantine table created");
    }

    /// <summary>
    /// Create indexes for optimal query performance
    /// </summary>
    private async Task CreateIndexesAsync(SqliteConnection conn)
    {
        Log.Debug("Creating import error handling indexes...");

        await CreateIndexIfNotExistsAsync(conn, "idx_import_errorlog_profile_execution",
            "ImportErrorLog", "(ImportProfileId, ExecutionId)");

        await CreateIndexIfNotExistsAsync(conn, "idx_import_errorlog_created",
            "ImportErrorLog", "(CreatedAt)");

        await CreateIndexIfNotExistsAsync(conn, "idx_import_quarantine_profile",
            "ImportQuarantine", "(ImportProfileId)");

        await CreateIndexIfNotExistsAsync(conn, "idx_import_quarantine_reviewed",
            "ImportQuarantine", "(ReviewedAt)");

        await CreateIndexIfNotExistsAsync(conn, "idx_import_quarantine_created",
            "ImportQuarantine", "(CreatedAt)");

        Log.Debug("✓ Indexes created");
    }

    /// <summary>
    /// Create an index if it doesn't already exist
    /// </summary>
    private async Task CreateIndexIfNotExistsAsync(
        SqliteConnection conn,
        string indexName,
        string tableName,
        string columns)
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
