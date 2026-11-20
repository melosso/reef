using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Reef.Core.ProfileDocuments.Models;

namespace Reef.Core.Workflows.Models;

public class WorkflowNodeDefinition
{
    public required string Type { get; init; }
    public required string Label { get; init; }
    public required string Icon { get; init; }
    public required string Tooltip { get; init; }
    public required string Group { get; init; }
    public string[] Inputs { get; init; } = WorkflowPorts.Single;
    public string[] Outputs { get; init; } = WorkflowPorts.Single;
    public List<NodeField> Fields { get; init; } = new();

    [JsonIgnore]
    public Func<JsonObject, string[]>? OutputsFor { get; init; }

    public string[] OutputPorts(JsonObject config) => OutputsFor?.Invoke(config) ?? Outputs;
}
