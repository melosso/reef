using Microsoft.Data.Sqlite;
using Dapper;
using Serilog;

namespace Reef.Core.Database;

// adds the CanvasLayoutJson column to the Profiles table for existing databases
public class ProfileCanvasMigration(string connectionString)
{
    public async Task ApplyAsync()
    {
        using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();

        var colNames = (await conn.QueryAsync("PRAGMA table_info(Profiles)"))
            .Select(r => (string)r.name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (!colNames.Contains("CanvasLayoutJson"))
        {
            await conn.ExecuteAsync("ALTER TABLE Profiles ADD COLUMN CanvasLayoutJson TEXT NULL");
            Log.Debug("Added column CanvasLayoutJson to Profiles");
        }

        Log.Debug("✓ Profile canvas migration completed");
    }
}
