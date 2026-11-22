using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Reef.Core.Nodes;
using Reef.Core.Nodes.Models;
using Reef.Core.Nodes.Exceptions;

namespace Reef.Core.Workflows.Models;

public record WorkflowNodeDefinition
{
    public required string Type { get; init; }
    public required string Label { get; init; }
    public required string Icon { get; init; }
    public required string Tooltip { get; init; }
    public required string Group { get; init; }
    public string[] Inputs { get; init; } = WorkflowPorts.Single;
    public string[] Outputs { get; init; } = WorkflowPorts.Single;
    public List<NodeField> Fields { get; init; } = new();
    public bool RunsWhenEmpty { get; init; }
    public string[] SqlFields { get; init; } = [];
    public string[] RawFields { get; init; } = [];

    [JsonIgnore]
    public Func<JsonObject, string[]>? OutputsFor { get; init; }

    public string[] OutputPorts(JsonObject config) => OutputsFor?.Invoke(config) ?? Outputs;
}
