namespace Reef.Core.Nodes.Models;

public record NodeError(string Message, string? NodeId = null, string? Field = null);
