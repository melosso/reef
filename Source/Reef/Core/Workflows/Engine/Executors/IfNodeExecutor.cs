using Reef.Core.Workflows.Models;

namespace Reef.Core.Workflows.Engine.Executors;

public class IfNodeExecutor : INodeExecutor
{
    public IReadOnlyCollection<string> Types { get; } = ["if"];

    public Task<NodeResult> ExecuteAsync(NodeContext context, CancellationToken ct)
    {
        var column = NodeConfig.Text(context.Config, "Column");
        var op = NodeConfig.Text(context.Config, "Operator");
        var value = NodeConfig.Text(context.Config, "Value");
        var input = context.Input;
        var matched = input.Items.ToLookup(row => RowValues.Matches(row.GetValueOrDefault(column), op, value));

        var result = new NodeResult();
        if (matched[true].Any())
            result.Outputs["true"] = input.WithRows(matched[true]);
        if (matched[false].Any())
            result.Outputs["false"] = input.WithRows(matched[false]);
        return Task.FromResult(result);
    }
}
