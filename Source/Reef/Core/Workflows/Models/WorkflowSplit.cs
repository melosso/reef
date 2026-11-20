namespace Reef.Core.Workflows.Models;

public record WorkflowSplit(string Column, string? FilenameTemplate, bool ExcludeColumn);
