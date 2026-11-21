namespace Reef.Core.Models.Dashboard;

/// <summary>
/// A successful run whose row count or duration is far from the workflow median
/// </summary>
public record AttentionAnomaly(int WorkflowId, string WorkflowName, int ExecutionId, string Reason, long Value, long Median, string StartedAt);
