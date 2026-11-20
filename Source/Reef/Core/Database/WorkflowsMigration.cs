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
        Log.Debug("Workflows migration completed");
    }
}
