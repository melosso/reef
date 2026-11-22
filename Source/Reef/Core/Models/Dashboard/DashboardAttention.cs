namespace Reef.Core.Models.Dashboard;

/// <summary>
/// What needs a person on the home page: failed workflows, unusual runs and jobs due soon
/// </summary>
public record DashboardAttention(
    IReadOnlyList<AttentionFailure> Failed,
    IReadOnlyList<AttentionAnomaly> Anomalies,
    IReadOnlyList<AttentionJob> UpNext,
    IReadOnlyList<AttentionWorkflow> UpNextWorkflows,
    AttentionTotals Last24h);
