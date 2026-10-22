namespace Reef.Core.ProfileDocuments.Models;

// Matches a value at Key that is in Values. A null Values matches any non-empty value.
public record NodeFieldCondition(string Key, string[]? Values = null);
