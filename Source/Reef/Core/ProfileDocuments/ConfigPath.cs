using System.Text.Json;
using System.Text.Json.Nodes;

namespace Reef.Core.ProfileDocuments;

public static class ConfigPath
{
    public static JsonNode? Get(JsonObject? root, string path)
    {
        JsonNode? current = root;
        foreach (var segment in path.Split('.'))
        {
            if (current is not JsonObject obj || !obj.TryGetPropertyValue(segment, out current))
                return null;
        }
        return current;
    }

    public static void Set(JsonObject root, string path, JsonNode? value)
    {
        var segments = path.Split('.');
        var current = root;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            if (current[segments[i]] is not JsonObject next)
            {
                next = new JsonObject();
                current[segments[i]] = next;
            }
            current = next;
        }

        if (value is null)
            current.Remove(segments[^1]);
        else
            current[segments[^1]] = value;
    }

    public static bool HasValue(JsonNode? node) => node switch
    {
        null => false,
        JsonValue v when v.GetValueKind() == JsonValueKind.String => !string.IsNullOrWhiteSpace(v.GetValue<string>()),
        JsonValue v when v.GetValueKind() is JsonValueKind.True or JsonValueKind.False => v.GetValue<bool>(),
        JsonArray a => a.Count > 0,
        JsonObject o => o.Count > 0,
        _ => true
    };

    public static string ToText(JsonNode? node) => node switch
    {
        null => "",
        JsonValue v when v.GetValueKind() == JsonValueKind.String => v.GetValue<string>(),
        JsonValue v when v.GetValueKind() == JsonValueKind.True => "true",
        JsonValue v when v.GetValueKind() == JsonValueKind.False => "false",
        _ => node.ToJsonString()
    };

    public static JsonObject? ParseObject(JsonNode? node)
    {
        if (node is JsonObject obj)
            return obj;
        if (node is not JsonValue value || value.GetValueKind() != JsonValueKind.String || value.GetValue<string>() is not { Length: > 0 } text)
            return null;
        try
        {
            return JsonNode.Parse(text) as JsonObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static JsonNode? ParseAny(JsonNode? node)
    {
        if (node is not JsonValue value || value.GetValueKind() != JsonValueKind.String || value.GetValue<string>() is not { Length: > 0 } text)
            return node;
        try
        {
            return JsonNode.Parse(text);
        }
        catch (JsonException)
        {
            return node;
        }
    }
}
