namespace Reef.Core.Models.Dashboard;

/// <summary>
/// One finished export or import run as read for the attention query
/// </summary>
public class AttentionRun
{
    public string Kind { get; init; } = "";
    public int ProfileId { get; init; }
    public string ProfileName { get; init; } = "";
    public bool DeltaSyncEnabled { get; init; }
    public int ExecutionId { get; init; }
    public string Status { get; init; } = "";
    public string StartedAt { get; init; } = "";
    public string? ErrorMessage { get; init; }
    public long? Rows { get; init; }
    public long? DurationMs { get; init; }
    public long Recent { get; init; }
}
