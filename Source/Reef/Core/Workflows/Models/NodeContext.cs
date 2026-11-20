using System.Text.Json.Nodes;

namespace Reef.Core.Workflows.Models;

public class NodeContext
{
    public required WorkflowDocument Workflow { get; init; }
    public required WorkflowNode Node { get; init; }
    public required int ExecutionId { get; init; }
    public required Dictionary<string, WorkflowItems> Inputs { get; init; }
    public required string WorkFolder { get; init; }
    public JsonObject Config => Node.Config ?? new JsonObject();

    public WorkflowItems Input => Inputs.GetValueOrDefault(WorkflowPorts.Main) ?? new WorkflowItems();
}
