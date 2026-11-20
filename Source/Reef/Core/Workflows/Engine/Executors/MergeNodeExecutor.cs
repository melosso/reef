using Reef.Core.Workflows.Models;

namespace Reef.Core.Workflows.Engine.Executors;

public class MergeNodeExecutor : INodeExecutor
{
    public IReadOnlyCollection<string> Types { get; } = ["merge"];

    public Task<NodeResult> ExecuteAsync(NodeContext context, CancellationToken ct)
    {
        var first = context.Inputs.GetValueOrDefault("input1") ?? new WorkflowItems();
        var second = context.Inputs.GetValueOrDefault("input2") ?? new WorkflowItems();
        var columns = first.Columns.Concat(second.Columns).DistinctBy(c => c.Name).ToList();
        var files = first.Files.Concat(second.Files).ToList();

        if (NodeConfig.Text(context.Config, "Mode") != "join")
            return Task.FromResult(NodeResult.Main(new WorkflowItems { Columns = columns, Items = [.. first.Items, .. second.Items], Files = files }));

        var key = NodeConfig.Text(context.Config, "JoinColumn");
        var lookup = second.Items.ToLookup(r => RowValues.Text(r.GetValueOrDefault(key)), StringComparer.OrdinalIgnoreCase);
        var joined = first.Items.SelectMany(left => lookup[RowValues.Text(left.GetValueOrDefault(key))].Select(right =>
        {
            var row = new Dictionary<string, object?>(right);
            foreach (var (name, value) in left)
                row[name] = value;
            return row;
        }));
        return Task.FromResult(NodeResult.Main(new WorkflowItems { Columns = columns, Items = [.. joined], Files = files }));
    }
}
