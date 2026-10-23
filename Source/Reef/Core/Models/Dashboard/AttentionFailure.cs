namespace Reef.Core.Models.Dashboard;

/// <summary>
/// A profile whose latest finished run failed
/// </summary>
public record AttentionFailure(string Kind, int ProfileId, string ProfileName, int ExecutionId, string Error, string StartedAt);
