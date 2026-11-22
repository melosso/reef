namespace Reef.Core.Models.Dashboard;

/// <summary>
/// A workflow whose latest finished run failed
/// </summary>
public record AttentionFailure(int WorkflowId, string WorkflowName, int ExecutionId, string Error, string StartedAt);
