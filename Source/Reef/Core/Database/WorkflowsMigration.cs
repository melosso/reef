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
                Iteration INTEGER NOT NULL DEFAULT 0,
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
        var nodeRunColumns = (await conn.QueryAsync("PRAGMA table_info(NodeRuns)")).Select(r => (string)r.name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!nodeRunColumns.Contains("Iteration"))
            await conn.ExecuteAsync("ALTER TABLE NodeRuns ADD COLUMN Iteration INTEGER NOT NULL DEFAULT 0");
        var deltaColumns = (await conn.QueryAsync("PRAGMA table_info(DeltaSyncState)")).Select(r => (string)r.name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (deltaColumns.Count > 0 && !deltaColumns.Contains("Scope"))
            await ScopeDeltaSyncStateAsync(conn, deltaColumns.Contains("Ulid"));
        var approvalColumns = (await conn.QueryAsync("PRAGMA table_info(PendingEmailApprovals)")).Select(r => (string)r.name).ToList();
        if (approvalColumns.Count > 0 && !approvalColumns.Contains("WorkflowExecutionId", StringComparer.OrdinalIgnoreCase))
            await ScopeApprovalsAsync(conn, approvalColumns);
        await MissingReferences.RemoveAsync(conn);
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
                    Ulid TEXT NULL
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

    // Workflow approvals have no profile
    private static async Task ScopeApprovalsAsync(SqliteConnection conn, List<string> columns)
    {
        var table = await conn.ExecuteScalarAsync<string>("SELECT sql FROM sqlite_master WHERE type = 'table' AND name = 'PendingEmailApprovals'") ?? "";
        var indexes = (await conn.QueryAsync<string>("SELECT sql FROM sqlite_master WHERE type = 'index' AND tbl_name = 'PendingEmailApprovals' AND sql IS NOT NULL")).ToList();
        var ddl = Replace(table, "PendingEmailApprovals", "PendingEmailApprovals_new");
        ddl = Replace(ddl, "ProfileId INTEGER NOT NULL", "ProfileId INTEGER NULL");
        ddl = Replace(ddl, "ProfileExecutionId INTEGER NOT NULL", "ProfileExecutionId INTEGER NULL");
        var constraints = ddl.IndexOf("FOREIGN KEY", StringComparison.OrdinalIgnoreCase);
        var close = ddl.LastIndexOf(')');
        ddl = constraints < 0
            ? string.Concat(ddl.AsSpan(0, close).TrimEnd(), ", WorkflowExecutionId INTEGER NULL, NodeId TEXT NULL, DestinationId INTEGER NULL, ApproverRoles TEXT NULL, FOREIGN KEY (WorkflowExecutionId) REFERENCES WorkflowExecutions(Id) ON DELETE CASCADE)")
            : string.Concat(ddl.AsSpan(0, constraints), "WorkflowExecutionId INTEGER NULL, NodeId TEXT NULL, DestinationId INTEGER NULL, ApproverRoles TEXT NULL, FOREIGN KEY (WorkflowExecutionId) REFERENCES WorkflowExecutions(Id) ON DELETE CASCADE, ", ddl.AsSpan(constraints));
        var list = string.Join(", ", columns);
        await conn.ExecuteAsync("PRAGMA foreign_keys = OFF");
        try
        {
            using var tx = conn.BeginTransaction();
            await conn.ExecuteAsync(ddl, transaction: tx);
            await conn.ExecuteAsync($"INSERT INTO PendingEmailApprovals_new ({list}) SELECT {list} FROM PendingEmailApprovals", transaction: tx);
            await conn.ExecuteAsync("DROP TABLE PendingEmailApprovals", transaction: tx);
            await conn.ExecuteAsync("ALTER TABLE PendingEmailApprovals_new RENAME TO PendingEmailApprovals", transaction: tx);
            foreach (var index in indexes)
                await conn.ExecuteAsync(index, transaction: tx);
            await conn.ExecuteAsync("CREATE INDEX IF NOT EXISTS IX_PendingEmailApprovals_WorkflowExecutionId ON PendingEmailApprovals(WorkflowExecutionId)", transaction: tx);
            tx.Commit();
        }
        finally
        {
            await conn.ExecuteAsync("PRAGMA foreign_keys = ON");
        }
        Log.Information("PendingEmailApprovals now accepts workflow approvals");
    }

    private static string Replace(string sql, string find, string replacement)
    {
        var at = sql.IndexOf(find, StringComparison.Ordinal);
        return at < 0
            ? throw new InvalidOperationException($"PendingEmailApprovals schema has no '{find}', migration stopped")
            : string.Concat(sql.AsSpan(0, at), replacement, sql.AsSpan(at + find.Length));
    }
}
