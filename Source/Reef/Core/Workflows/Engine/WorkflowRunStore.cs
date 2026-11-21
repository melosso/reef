using Dapper;
using Microsoft.Data.Sqlite;
using Reef.Core.Workflows.Models;

namespace Reef.Core.Workflows.Engine;

public class WorkflowRunStore(DatabaseConfig config)
{
    public async Task<int> StartAsync(WorkflowExecution run, CancellationToken ct)
    {
        await using var db = new SqliteConnection(config.ConnectionString);
        return await db.ExecuteScalarAsync<int>(new CommandDefinition("""
            INSERT INTO WorkflowExecutions (WorkflowId, WorkflowVersion, TriggerNodeId, Status, StartedAt)
            VALUES (@WorkflowId, @WorkflowVersion, @TriggerNodeId, @Status, @StartedAt) RETURNING Id
            """, run, cancellationToken: ct));
    }

    public async Task CompleteAsync(WorkflowExecution run)
    {
        await using var db = new SqliteConnection(config.ConnectionString);
        await db.ExecuteAsync("UPDATE WorkflowExecutions SET Status = @Status, CompletedAt = @CompletedAt, ErrorMessage = @ErrorMessage WHERE Id = @Id", run);
    }

    public async Task SaveNodeAsync(NodeRun node)
    {
        await using var db = new SqliteConnection(config.ConnectionString);
        node.Id = await db.ExecuteScalarAsync<int>("""
            INSERT INTO NodeRuns (WorkflowExecutionId, NodeId, Iteration, Status, StartedAt, CompletedAt, RowCount, OutputSampleJson, ErrorMessage)
            VALUES (@WorkflowExecutionId, @NodeId, @Iteration, @Status, @StartedAt, @CompletedAt, @RowCount, @OutputSampleJson, @ErrorMessage) RETURNING Id
            """, node);
    }

    public async Task<DateTime?> LastStartedAsync(int workflowId, string triggerNodeId, CancellationToken ct)
    {
        await using var db = new SqliteConnection(config.ConnectionString);
        return await db.ExecuteScalarAsync<DateTime?>(new CommandDefinition(
            "SELECT MAX(StartedAt) FROM WorkflowExecutions WHERE WorkflowId = @workflowId AND TriggerNodeId = @triggerNodeId", new { workflowId, triggerNodeId }, cancellationToken: ct));
    }

    public async Task<bool> IsRunningAsync(int workflowId, CancellationToken ct)
    {
        await using var db = new SqliteConnection(config.ConnectionString);
        return await db.ExecuteScalarAsync<bool>(new CommandDefinition(
            "SELECT EXISTS (SELECT 1 FROM WorkflowExecutions WHERE WorkflowId = @workflowId AND Status = @status)", new { workflowId, status = WorkflowRunStatus.Running }, cancellationToken: ct));
    }

    public async Task<WorkflowExecution?> GetAsync(int id, CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(config.ConnectionString);
        var run = await db.QuerySingleOrDefaultAsync<WorkflowExecution>(new CommandDefinition("SELECT * FROM WorkflowExecutions WHERE Id = @id", new { id }, cancellationToken: ct));
        if (run is not null)
            run.Nodes = [.. await db.QueryAsync<NodeRun>(new CommandDefinition("SELECT * FROM NodeRuns WHERE WorkflowExecutionId = @id ORDER BY Id", new { id }, cancellationToken: ct))];
        return run;
    }

    public async Task<List<WorkflowExecution>> ListAsync(int workflowId, int limit, CancellationToken ct = default)
    {
        await using var db = new SqliteConnection(config.ConnectionString);
        return [.. await db.QueryAsync<WorkflowExecution>(new CommandDefinition(
            "SELECT * FROM WorkflowExecutions WHERE WorkflowId = @workflowId ORDER BY Id DESC LIMIT @limit", new { workflowId, limit }, cancellationToken: ct))];
    }
}
