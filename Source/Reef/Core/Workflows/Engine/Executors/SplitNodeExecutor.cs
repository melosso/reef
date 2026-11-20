using Reef.Core.Workflows.Exceptions;
using Reef.Core.Workflows.Models;

namespace Reef.Core.Workflows.Engine.Executors;

public class SplitNodeExecutor : INodeExecutor
{
    public IReadOnlyCollection<string> Types { get; } = ["split"];

    public Task<NodeResult> ExecuteAsync(NodeContext context, CancellationToken ct)
    {
        var column = NodeConfig.Text(context.Config, "SplitKeyColumn");
        var input = context.Input;
        if (input.Items.Count > 0 && !input.Items[0].ContainsKey(column))
            throw new NodeExecutionException($"Split column {column} is not in the rows, found {string.Join(", ", input.Items[0].Keys)}");
        var split = new WorkflowSplit(column, NodeConfig.Text(context.Config, "SplitFilenameTemplate") is { Length: > 0 } name ? name : null,
            NodeConfig.Flag(context.Config, "ExcludeSplitKeyFromOutput"));
        return Task.FromResult(NodeResult.Main(new WorkflowItems { Columns = input.Columns, Items = input.Items, Files = input.Files, Split = split }));
    }
}
