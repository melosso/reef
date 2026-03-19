using Microsoft.Data.Sqlite;
using Dapper;
using Serilog;

namespace Reef.Core.Database;

/// <summary>
/// Migration to add MFA columns to the Users table
/// </summary>
public class MfaMigration(string connectionString)
{
    public async Task ApplyAsync()
    {
        using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();

        await AddColumnIfMissingAsync(conn, "Users", "Email", "TEXT NULL");
        await AddColumnIfMissingAsync(conn, "Users", "MfaEnabled", "INTEGER NOT NULL DEFAULT 0");
        await AddColumnIfMissingAsync(conn, "Users", "MfaMethod", "TEXT NULL");
        await AddColumnIfMissingAsync(conn, "Users", "TotpSecret", "TEXT NULL");
        await AddColumnIfMissingAsync(conn, "Users", "PendingTotpSecret", "TEXT NULL");
        await AddColumnIfMissingAsync(conn, "Users", "BackupCodes", "TEXT NULL");

        Log.Debug("✓ MFA migration completed");
    }

    private static async Task AddColumnIfMissingAsync(SqliteConnection conn, string table, string column, string definition)
    {
        var colNames = (await conn.QueryAsync($"PRAGMA table_info({table})"))
            .Select(r => (string)r.name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!colNames.Contains(column))
        {
            await conn.ExecuteAsync($"ALTER TABLE {table} ADD COLUMN {column} {definition}");
            Log.Debug("Added column {Column} to {Table}", column, table);
        }
    }
}
