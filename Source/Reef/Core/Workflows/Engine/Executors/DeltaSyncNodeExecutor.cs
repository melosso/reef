using Reef.Core.Models;
using Reef.Core.Services;
using Reef.Core.Workflows.Exceptions;
using Reef.Core.Workflows.Models;

namespace Reef.Core.Workflows.Engine.Executors;

public class DeltaSyncNodeExecutor(DeltaSyncService deltas) : INodeExecutor
{
    public IReadOnlyCollection<string> Types { get; } = ["deltasync"];

    public static string Scope(int workflowId, string nodeId) => $"workflow:{workflowId}:{nodeId}";

    public async Task<NodeResult> ExecuteAsync(NodeContext context, CancellationToken ct)
    {
        var config = context.Config;
        var column = NodeConfig.Text(config, "DeltaSyncReefIdColumn");
        var input = context.Input;
        if (input.Items.Count > 0 && !input.Items[0].ContainsKey(column))
            throw new NodeExecutionException($"Unique id column {column} is not in the rows");

        var settings = new DeltaSyncConfig
        {
            Enabled = true,
            ReefIdColumn = column,
            HashAlgorithm = Or(NodeConfig.Text(config, "DeltaSyncHashAlgorithm"), "SHA256"),
            IncludeDeleted = NodeConfig.Flag(config, "DeltaSyncTrackDeletes"),
            DuplicateStrategy = Or(NodeConfig.Text(config, "DeltaSyncDuplicateStrategy"), "Strict"),
            NullStrategy = Or(NodeConfig.Text(config, "DeltaSyncNullStrategy"), "Strict"),
            NumericPrecision = int.TryParse(NodeConfig.Text(config, "DeltaSyncNumericPrecision"), out var precision) ? precision : 6
        };
        var scope = Scope(context.Workflow.Id ?? 0, context.Node.Id);
        var rows = input.Items.Select(r => r.ToDictionary(c => c.Key, c => c.Value ?? DBNull.Value)).ToList();
        var result = await deltas.ProcessDeltaAsync(0, rows, settings, scope);

        var passed = result.NewRows.Concat(result.ChangedRows).Select(r => r.ToDictionary(c => c.Key, c => (object?)c.Value)).ToList();
        if (settings.IncludeDeleted)
            passed.AddRange(result.DeletedReefIds.Select(id => new Dictionary<string, object?> { [column] = id, ["_ReefDeleted"] = true, ["_ReefDeletedAt"] = DateTime.UtcNow }));
        if (NodeConfig.Flag(config, "ExcludeReefIdFromOutput"))
            passed.ForEach(r => r.Remove(column));

        var retention = int.TryParse(NodeConfig.Text(config, "DeltaSyncRetentionDays"), out var days) ? days : (int?)null;
        return new NodeResult
        {
            Outputs = { [WorkflowPorts.Main] = input.WithRows(passed) },
            OnRunSuccess = async () =>
            {
                await deltas.CommitDeltaSyncAsync(0, context.ExecutionId, result, scope);
                if (retention is > 0)
                    await deltas.CleanupOldStateAsync(0, retention.Value, scope);
            }
        };
    }

    private static string Or(string value, string fallback) => value.Length > 0 ? value : fallback;
}
