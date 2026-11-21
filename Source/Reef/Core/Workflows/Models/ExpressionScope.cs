namespace Reef.Core.Workflows.Models;

public record ExpressionScope(
    WorkflowDocument Workflow,
    int ExecutionId,
    IReadOnlyDictionary<string, Dictionary<string, WorkflowItems>> Nodes,
    int RunIndex,
    Dictionary<string, object?>? Item = null,
    int ItemIndex = 0);
