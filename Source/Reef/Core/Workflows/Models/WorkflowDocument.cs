namespace Reef.Core.Workflows.Models;

public class WorkflowDocument
{
    public int? Id { get; set; }
    public string Name { get; set; } = "";
    public int Version { get; set; }
    public List<WorkflowNode> Nodes { get; set; } = new();
    public List<WorkflowEdge> Edges { get; set; } = new();
    public int MaxPasses { get; set; } = 1000;
    public DateTime? UpdatedAt { get; set; }
}
