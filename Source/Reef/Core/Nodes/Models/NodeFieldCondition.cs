namespace Reef.Core.Nodes.Models;

// Matches a value at Key that is in Values. A null Values matches any non-empty value.
public record NodeFieldCondition(string Key, string[]? Values = null);
