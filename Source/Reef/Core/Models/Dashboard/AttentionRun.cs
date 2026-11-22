namespace Reef.Core.Models.Dashboard;

/// <summary>
/// One finished workflow run as read for the attention query
/// </summary>
public class AttentionRun
{
    public int WorkflowId { get; init; }
    public string WorkflowName { get; init; } = "";
    public bool DeltaSyncEnabled { get; init; }
    public int ExecutionId { get; init; }
    public string Status { get; init; } = "";
    public string StartedAt { get; init; } = "";
    public string? ErrorMessage { get; init; }
    public long? Rows { get; init; }
    public long? DurationMs { get; init; }
    public long Recent { get; init; }
}
