using System.Text.Json.Nodes;
using Reef.Core.ProfileDocuments.Models;

namespace Reef.Core.ProfileDocuments;

// reads and writes catalog field keys on a serialized profile, where a dotted key reaches into a column that stores a json string
public static class EntityFields
{
    public static string Column(string key) => key.Split('.', 2)[0];

    public static JsonNode? Read(JsonObject entity, NodeField field)
    {
        var parts = field.Key.Split('.', 2);
        var column = entity[parts[0]];
        if (parts.Length == 2)
            return ConfigPath.Get(ConfigPath.ParseObject(column), parts[1])?.DeepClone();
        return IsStructured(field) ? ConfigPath.ParseAny(column)?.DeepClone() : column?.DeepClone();
    }

    public static void Write(JsonObject entity, NodeField field, JsonNode? value, JsonObject defaults)
    {
        var parts = field.Key.Split('.', 2);
        if (parts.Length == 2)
        {
            var obj = ConfigPath.ParseObject(entity[parts[0]])?.DeepClone().AsObject() ?? new JsonObject();
            ConfigPath.Set(obj, parts[1], value);
            entity[parts[0]] = obj.Count == 0 ? null : JsonValue.Create(obj.ToJsonString());
            return;
        }

        if (value is null)
            entity[parts[0]] = defaults[parts[0]]?.DeepClone();
        else if (IsStructured(field))
            entity[parts[0]] = value.GetValueKind() == System.Text.Json.JsonValueKind.String ? value : JsonValue.Create(value.ToJsonString());
        else
            entity[parts[0]] = value;
    }

    public static void Reset(JsonObject entity, string column, JsonObject defaults) =>
        entity[column] = defaults[column]?.DeepClone();

    private static bool IsStructured(NodeField field) =>
        field.Kind is NodeFieldKinds.Json or NodeFieldKinds.Table;
}
