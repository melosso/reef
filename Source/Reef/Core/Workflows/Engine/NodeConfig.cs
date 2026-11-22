using System.Text.Json;
using System.Text.Json.Nodes;
using Reef.Core.Workflows.Exceptions;
using Reef.Core.Nodes;
using Reef.Core.Nodes.Models;
using Reef.Core.Nodes.Exceptions;

namespace Reef.Core.Workflows.Engine;

public static class NodeConfig
{
    public static string Text(JsonObject config, string key) => ConfigPath.ToText(ConfigPath.Get(config, key));

    public static bool Flag(JsonObject config, string key) => ConfigPath.Get(config, key)?.GetValueKind() == JsonValueKind.True;

    public static int Id(JsonObject config, string key) =>
        ConfigPath.Get(config, key) is JsonValue value && value.GetValueKind() == JsonValueKind.Number && value.GetValue<int>() > 0
            ? value.GetValue<int>()
            : throw new NodeExecutionException($"{key} is not set");
}
