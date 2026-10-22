using System.Text.Json.Nodes;

namespace Reef.Core.ProfileDocuments.Models;

public class NodeField
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public string Kind { get; init; } = NodeFieldKinds.Text;
    public string? Tooltip { get; init; }
    public string? Placeholder { get; init; }
    public bool Required { get; init; }
    public bool Hidden { get; init; }
    public string? Format { get; init; }
    public List<NodeFieldOption>? Options { get; init; }
    public string? OptionsSource { get; init; }
    public NodeFieldCondition? ShowWhen { get; init; }
    public JsonNode? Default { get; set; }
    public List<NodeField>? Columns { get; init; }
}
