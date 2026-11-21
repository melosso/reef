namespace Reef.Core.Workflows.Models;

public record PendingNode(WorkflowNode Node, Dictionary<string, WorkflowItems> Inputs);
