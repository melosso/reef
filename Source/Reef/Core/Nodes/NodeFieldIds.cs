using System.Text.Json;
using System.Text.Json.Nodes;
using Reef.Core.Nodes.Models;
using Reef.Core.PublicIds;

namespace Reef.Core.Nodes;

// Unknown ids stay for validation
public sealed class NodeFieldIds(PublicIdMap ids)
{
    private readonly EmbeddedIds _embedded = new(ids);

    private static readonly Dictionary<string, string> Sources = new()
    {
        ["connections"] = "Connections",
        ["destinations"] = "Destinations",
        ["email-destinations"] = "Destinations",
        ["groups"] = "ProfileGroups",
        ["templates"] = "QueryTemplates"
    };

    public void Rewrite(IEnumerable<NodeField> fields, JsonObject config, bool toPublic)
    {
        foreach (var field in fields)
        {
            var value = ConfigPath.Get(config, field.Key);
            if (field.Columns is { } columns && value is JsonArray rows)
            {
                foreach (var row in rows.OfType<JsonObject>())
                {
                    Rewrite(columns, row, toPublic);
                }
            }
            else if (EmbeddedIds.Handles(field.Key) && value is JsonObject embedded)
            {
                _embedded.Rewrite(field.Key, embedded, toPublic);
            }
            else if (EmbeddedIds.Handles(field.Key) && value is JsonValue text && text.GetValueKind() == JsonValueKind.String)
            {
                var raw = text.GetValue<string>();
                ConfigPath.Set(config, field.Key, (toPublic ? _embedded.ToPublic(field.Key, raw) : _embedded.ToInternal(field.Key, raw)) ?? raw);
            }
            else if (field.OptionsSource is { } source && Sources.TryGetValue(source, out var table) && value is JsonValue single)
            {
                ConfigPath.Set(config, field.Key, toPublic ? Public(table, single) : Internal(table, single));
            }
        }
    }

    private JsonNode? Public(string table, JsonValue value) => value.GetValueKind() != JsonValueKind.Number
        ? value.DeepClone()
        : ids.ToUlid(table, long.Parse(value.ToJsonString())) is { } ulid ? JsonValue.Create(ulid) : null;

    private JsonNode Internal(string table, JsonValue value) =>
        value.GetValueKind() == JsonValueKind.String && ids.TryToId(table, value.GetValue<string>(), out var id) ? JsonValue.Create(id) : value.DeepClone();
}
