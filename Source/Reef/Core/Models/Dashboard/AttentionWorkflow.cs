namespace Reef.Core.Models.Dashboard;

/// <summary>
/// An active workflow whose schedule step is due within the next 24 hours
/// </summary>
public record AttentionWorkflow(int WorkflowId, string WorkflowName, string NextRunTime);
