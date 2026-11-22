namespace Reef.Core.Workflows.Models;

public class WorkflowCatalog
{
    public required List<WorkflowNodeDefinition> Nodes { get; init; }

    public WorkflowNodeDefinition? Find(string type) => Nodes.FirstOrDefault(n => n.Type == type);
}
