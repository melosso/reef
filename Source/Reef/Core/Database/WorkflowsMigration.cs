using Microsoft.Data.Sqlite;
using Dapper;
using Serilog;

namespace Reef.Core.Database;

// Workflow graphs live as one json document per row, the columns hold what is listed or looked up.
public class WorkflowsMigration(string connectionString)
{
    public async Task ApplyAsync()
    {
        using var conn = new SqliteConnection(connectionString);
        await conn.OpenAsync();
        await conn.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS Workflows (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL COLLATE NOCASE,
                DocumentJson TEXT NOT NULL,
                Version INTEGER NOT NULL DEFAULT 1,
                Hash TEXT NOT NULL,
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL,
                CreatedBy INTEGER NULL
            );
            CREATE UNIQUE INDEX IF NOT EXISTS IX_Workflows_Name ON Workflows (Name);
            """);
        Log.Debug("Workflows migration completed");
    }
}
