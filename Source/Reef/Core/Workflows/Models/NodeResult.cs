namespace Reef.Core.Workflows.Models;

// Missing port skips downstream nodes
public class NodeResult
{
    public Dictionary<string, WorkflowItems> Outputs { get; init; } = new();
    public string? Warning { get; init; }
    public Func<Task>? OnRunSuccess { get; init; }

    public static NodeResult Main(WorkflowItems items) => new() { Outputs = { [WorkflowPorts.Main] = items } };
}
