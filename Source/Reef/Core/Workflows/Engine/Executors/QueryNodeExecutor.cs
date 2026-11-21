using Reef.Core.Services;
using Reef.Core.Workflows.Exceptions;
using Reef.Core.Workflows.Models;

namespace Reef.Core.Workflows.Engine.Executors;

public class QueryNodeExecutor(ConnectionService connections, QueryExecutor queries) : INodeExecutor
{
    public IReadOnlyCollection<string> Types { get; } = ["query"];

    public async Task<NodeResult> ExecuteAsync(NodeContext context, CancellationToken ct)
    {
        var connection = await connections.GetByIdAsync(NodeConfig.Id(context.Config, "ConnectionId"))
            ?? throw new NodeExecutionException("Connection not found");
        if (!connection.IsActive)
            throw new NodeExecutionException($"Connection {connection.Name} is inactive");

        var (sql, parameters) = context.Sql("Query");
        var (success, rows, error, _) = await queries.ExecuteQueryAsync(connection, sql, parameters, ct: ct);
        if (!success)
            throw new NodeExecutionException(error ?? "Query failed");
        return NodeResult.Main(WorkflowItems.FromRows(rows.Select(r => r.ToDictionary(c => c.Key, c => (object?)c.Value))));
    }
}
