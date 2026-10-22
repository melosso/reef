using System.Text.Json.Nodes;
using Reef.Core.ProfileDocuments.Models;

namespace Reef.Core.ProfileDocuments;

public static class NodeConditions
{
    public static bool Matches(NodeFieldCondition? condition, Func<string, JsonNode?> read)
    {
        if (condition is null)
            return true;
        var value = read(condition.Key);
        return condition.Values is null
            ? ConfigPath.HasValue(value)
            : condition.Values.Contains(ConfigPath.ToText(value), StringComparer.OrdinalIgnoreCase);
    }
}
