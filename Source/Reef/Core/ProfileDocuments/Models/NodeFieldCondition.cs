namespace Reef.Core.ProfileDocuments.Models;

// matches when the value at Key is one of Values, or has any value when Values is null
public record NodeFieldCondition(string Key, string[]? Values = null);
