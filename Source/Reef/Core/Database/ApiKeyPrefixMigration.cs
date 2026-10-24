using Dapper;
using Microsoft.Data.Sqlite;
using Serilog;

namespace Reef.Core.Database;

// Keys hashed with BCrypt have no prefix and stay unusable
public class ApiKeyPrefixMigration(string connectionString)
{
    public async Task ApplyAsync()
    {
        await using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();

        var columns = (await conn.QueryAsync("PRAGMA table_info(ApiKeys)")).Select(r => (string)r.name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!columns.Contains("KeyPrefix"))
        {
            await conn.ExecuteAsync("ALTER TABLE ApiKeys ADD COLUMN KeyPrefix TEXT NULL");
            Log.Debug("Added column KeyPrefix to ApiKeys");
        }
        await conn.ExecuteAsync("CREATE UNIQUE INDEX IF NOT EXISTS idx_apikeys_prefix ON ApiKeys(KeyPrefix) WHERE KeyPrefix IS NOT NULL");
    }
}
