using System.Text.Json.Nodes;
using Reef.Core.Nodes;
using Reef.Core.Workflows.Engine;

namespace Reef.Core.Workflows.Models;

public record NodeContext
{
    public required WorkflowDocument Workflow { get; init; }
    public required WorkflowNode Node { get; init; }
    public required int ExecutionId { get; init; }
    public required Dictionary<string, WorkflowItems> Inputs { get; init; }
    public required string WorkFolder { get; init; }
    public required JsonObject Config { get; init; }
    public required ExpressionScope Scope { get; init; }
    public Dictionary<string, object> State { get; init; } = new();

    public WorkflowItems Input => Inputs.GetValueOrDefault(WorkflowPorts.Main) ?? new WorkflowItems();

    public (string Sql, Dictionary<string, string> Parameters) Sql(string key) =>
        WorkflowExpressions.Bind(ConfigPath.ToText(ConfigPath.Get(Config, key)), Scope, key);
}
