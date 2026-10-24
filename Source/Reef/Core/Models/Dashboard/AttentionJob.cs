namespace Reef.Core.Models.Dashboard;

/// <summary>
/// An enabled job due within the next 24 hours
/// </summary>
public record AttentionJob(long Id, string Name, string NextRunTime);
