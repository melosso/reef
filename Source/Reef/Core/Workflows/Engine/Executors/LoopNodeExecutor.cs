using Reef.Core.Workflows.Catalogs;
using Reef.Core.Workflows.Models;

namespace Reef.Core.Workflows.Engine.Executors;

public class LoopNodeExecutor : INodeExecutor
{
    private const string Remaining = "remaining";
    private const string Processed = "processed";

    public IReadOnlyCollection<string> Types { get; } = [WorkflowNodeCatalog.Loop];

    public Task<NodeResult> ExecuteAsync(NodeContext context, CancellationToken ct)
    {
        var input = context.Input;
        if (context.State.GetValueOrDefault(Remaining) is not List<Dictionary<string, object?>> remaining || context.State[Processed] is not List<Dictionary<string, object?>> processed)
        {
            remaining = [.. input.Items];
            processed = [];
            context.State[Remaining] = remaining;
            context.State[Processed] = processed;
        }
        else
            processed.AddRange(input.Items);

        if (remaining.Count == 0)
        {
            context.State.Clear();
            return Task.FromResult(new NodeResult { Outputs = { [WorkflowNodeCatalog.LoopDone] = WorkflowItems.FromRows(processed) } });
        }

        var size = Math.Min(int.TryParse(NodeConfig.Text(context.Config, "BatchSize"), out var batch) && batch > 0 ? batch : 1, remaining.Count);
        var rows = remaining.GetRange(0, size);
        remaining.RemoveRange(0, size);
        return Task.FromResult(new NodeResult { Outputs = { [WorkflowNodeCatalog.LoopBatch] = input.WithRows(rows) } });
    }
}
