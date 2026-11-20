namespace Reef.Core.Workflows.Models;

public class WorkflowDocument
{
    public int? Id { get; set; }
    public string Name { get; set; } = "";
    public int Version { get; set; }
    public List<WorkflowNode> Nodes { get; set; } = new();
    public List<WorkflowEdge> Edges { get; set; } = new();
    public DateTime? UpdatedAt { get; set; }
}
