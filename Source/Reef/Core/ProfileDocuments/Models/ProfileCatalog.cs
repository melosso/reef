namespace Reef.Core.ProfileDocuments.Models;

public class ProfileCatalog
{
    public required string Kind { get; init; }
    public required List<NodeField> Settings { get; init; }
    public required List<NodeDefinition> Nodes { get; init; }
    public List<string[]> RequireOneOf { get; init; } = new();
}
