namespace Reef.Core.Workflows.Models;

public record WorkflowBatch(string? SplitKey, List<Dictionary<string, object>> Rows);
