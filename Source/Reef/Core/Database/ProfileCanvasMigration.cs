using Microsoft.Data.Sqlite;
using Dapper;
using Serilog;

namespace Reef.Core.Database;

// adds the CanvasLayoutJson column to the Profiles and ImportProfiles tables for existing databases
public class ProfileCanvasMigration(string connectionString)
{
    public async Task ApplyAsync()
    {
        using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();

        foreach (var table in new[] { "Profiles", "ImportProfiles" })
        {
            var colNames = (await conn.QueryAsync($"PRAGMA table_info({table})"))
                .Select(r => (string)r.name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (!colNames.Contains("CanvasLayoutJson"))
            {
                await conn.ExecuteAsync($"ALTER TABLE {table} ADD COLUMN CanvasLayoutJson TEXT NULL");
                Log.Debug("Added column CanvasLayoutJson to {Table}", table);
            }
        }

        Log.Debug("✓ Profile canvas migration completed");
    }
}
