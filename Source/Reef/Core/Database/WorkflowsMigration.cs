using Microsoft.Data.Sqlite;
using Dapper;
using Serilog;

namespace Reef.Core.Database;

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
            CREATE TABLE IF NOT EXISTS WorkflowExecutions (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                WorkflowId INTEGER NOT NULL,
                WorkflowVersion INTEGER NOT NULL,
                TriggerNodeId TEXT NOT NULL,
                Status TEXT NOT NULL,
                StartedAt TEXT NOT NULL,
                CompletedAt TEXT NULL,
                ErrorMessage TEXT NULL,
                FOREIGN KEY (WorkflowId) REFERENCES Workflows(Id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS IX_WorkflowExecutions_WorkflowId ON WorkflowExecutions (WorkflowId, StartedAt);
            CREATE TABLE IF NOT EXISTS NodeRuns (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                WorkflowExecutionId INTEGER NOT NULL,
                NodeId TEXT NOT NULL,
                Status TEXT NOT NULL,
                StartedAt TEXT NOT NULL,
                CompletedAt TEXT NULL,
                RowCount INTEGER NOT NULL DEFAULT 0,
                OutputSampleJson TEXT NULL,
                ErrorMessage TEXT NULL,
                FOREIGN KEY (WorkflowExecutionId) REFERENCES WorkflowExecutions(Id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS IX_NodeRuns_Execution ON NodeRuns (WorkflowExecutionId);
            """);
        var deltaColumns = (await conn.QueryAsync("PRAGMA table_info(DeltaSyncState)")).Select(r => (string)r.name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (deltaColumns.Count > 0 && !deltaColumns.Contains("Scope"))
            await ScopeDeltaSyncStateAsync(conn, deltaColumns.Contains("Ulid"));
        Log.Debug("Workflows migration completed");
    }

    // Workflow rows have no profile
    private static async Task ScopeDeltaSyncStateAsync(SqliteConnection conn, bool hasUlid)
    {
        var columns = "Id, ProfileId, ReefId, RowHash, LastSeenExecutionId, FirstSeenAt, LastSeenAt, IsDeleted, DeletedAt" + (hasUlid ? ", Ulid" : "");
        await conn.ExecuteAsync("PRAGMA foreign_keys = OFF");
        try
        {
            using var tx = conn.BeginTransaction();
            await conn.ExecuteAsync($"""
                CREATE TABLE DeltaSyncState_new (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ProfileId INTEGER NULL,
                    Scope TEXT NULL,
                    ReefId TEXT NOT NULL,
                    RowHash TEXT NOT NULL,
                    LastSeenExecutionId INTEGER NULL,
                    FirstSeenAt TEXT NOT NULL DEFAULT (datetime('now')),
                    LastSeenAt TEXT NOT NULL DEFAULT (datetime('now')),
                    IsDeleted INTEGER NOT NULL DEFAULT 0,
                    DeletedAt TEXT NULL,
                    Ulid TEXT NULL,
                    FOREIGN KEY (ProfileId) REFERENCES Profiles(Id) ON DELETE CASCADE,
                    FOREIGN KEY (LastSeenExecutionId) REFERENCES ProfileExecutions(Id)
                );
                INSERT INTO DeltaSyncState_new ({columns}) SELECT {columns} FROM DeltaSyncState;
                DROP TABLE DeltaSyncState;
                ALTER TABLE DeltaSyncState_new RENAME TO DeltaSyncState;
                CREATE INDEX IF NOT EXISTS idx_deltasync_profile_reefid ON DeltaSyncState(ProfileId, ReefId);
                CREATE INDEX IF NOT EXISTS idx_deltasync_profile_hash ON DeltaSyncState(ProfileId, RowHash);
                CREATE INDEX IF NOT EXISTS idx_deltasync_execution ON DeltaSyncState(LastSeenExecutionId);
                CREATE INDEX IF NOT EXISTS idx_deltasync_deleted ON DeltaSyncState(ProfileId, IsDeleted);
                CREATE INDEX IF NOT EXISTS idx_deltasync_scope_reefid ON DeltaSyncState(Scope, ReefId);
                """, transaction: tx);
            tx.Commit();
        }
        finally
        {
            await conn.ExecuteAsync("PRAGMA foreign_keys = ON");
        }
        Log.Information("DeltaSyncState now accepts workflow scopes");
    }
}
