using Reef.Core.Workflows.Catalogs;
using Reef.Core.Workflows.Models;

namespace Reef.Core.Workflows.Engine.Executors;

public class SwitchNodeExecutor : INodeExecutor
{
    public IReadOnlyCollection<string> Types { get; } = ["switch"];

    public Task<NodeResult> ExecuteAsync(NodeContext context, CancellationToken ct)
    {
        var column = NodeConfig.Text(context.Config, "Column");
        var cases = WorkflowNodeCatalog.Catalog.Find("switch")!.OutputPorts(context.Config).Where(p => p != WorkflowNodeCatalog.Fallback).ToList();
        var input = context.Input;
        var routed = input.Items.GroupBy(row =>
            cases.FirstOrDefault(c => RowValues.Matches(row.GetValueOrDefault(column), "equals", c)) ?? WorkflowNodeCatalog.Fallback);

        var result = new NodeResult();
        foreach (var group in routed)
            result.Outputs[group.Key] = input.WithRows(group);
        return Task.FromResult(result);
    }
}
