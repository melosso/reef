namespace Reef.Core.Workflows.Models;

public class WorkflowExecution
{
    public int Id { get; set; }
    public int WorkflowId { get; set; }
    public int WorkflowVersion { get; set; }
    public string TriggerNodeId { get; set; } = "";
    public string Status { get; set; } = WorkflowRunStatus.Running;
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string? ErrorMessage { get; set; }
    public string? WorkflowName { get; set; }
    public List<NodeRun> Nodes { get; set; } = new();
}
