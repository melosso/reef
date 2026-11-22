namespace Reef.Core.Workflows.Models;

public sealed record WorkflowRow(long Id, string Name, long Version, string DocumentJson, string UpdatedAt);
