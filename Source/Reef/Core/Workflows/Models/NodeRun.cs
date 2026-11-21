namespace Reef.Core.Workflows.Models;

public class NodeRun
{
    public int Id { get; set; }
    public int WorkflowExecutionId { get; set; }
    public string NodeId { get; set; } = "";
    public int Iteration { get; set; }
    public string Status { get; set; } = WorkflowRunStatus.Running;
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int RowCount { get; set; }
    public string? OutputSampleJson { get; set; }
    public string? ErrorMessage { get; set; }
}
