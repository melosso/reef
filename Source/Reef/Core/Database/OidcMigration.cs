using Microsoft.Data.Sqlite;
using Dapper;
using Serilog;

namespace Reef.Core.Database;

/// <summary>
/// Migration to add the OidcSubject column to the Users table for existing databases
/// </summary>
public class OidcMigration(string connectionString)
{
    public async Task ApplyAsync()
    {
        using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();

        await AddColumnIfMissingAsync(conn, "Users", "OidcSubject", "TEXT NOT NULL DEFAULT ''");

        Log.Debug("✓ OIDC migration completed");
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
