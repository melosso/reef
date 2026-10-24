namespace Reef.Core.Models.Dashboard;

/// <summary>
/// A successful run whose row count or duration is far from the profile's median
/// </summary>
public record AttentionAnomaly(string Kind, int ProfileId, string ProfileName, int ExecutionId, string Reason, long Value, long Median, string StartedAt);
