using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Reef.Core.ProfileDocuments.Models;

public class NodeDefinition
{
    public required string Type { get; init; }
    public required string Label { get; init; }
    public required string Icon { get; init; }
    public required string Tooltip { get; init; }
    public required int Rank { get; init; }
    public bool Required { get; init; }
    public NodeFieldCondition? Presence { get; init; }
    public string[] Excludes { get; init; } = [];
    public string[] Requires { get; init; } = [];
    public Dictionary<string, JsonNode?> Sets { get; init; } = new();
    public List<NodeField> Fields { get; init; } = new();

    // Derives dependent config values before mapping.
    [JsonIgnore]
    public Action<JsonObject>? Normalize { get; init; }
}
