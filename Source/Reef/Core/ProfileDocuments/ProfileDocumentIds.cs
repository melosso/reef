using System.Text.Json;
using System.Text.Json.Nodes;
using Reef.Core.ProfileDocuments.Models;
using Reef.Core.PublicIds;

namespace Reef.Core.ProfileDocuments;

// Swaps the integer ids behind list fields in a document for Ulids on the way out and back on the way in.
public sealed class ProfileDocumentIds(PublicIdMap ids)
{
    private static readonly Dictionary<string, string> Sources = new()
    {
        ["connections"] = "Connections",
        ["destinations"] = "Destinations",
        ["email-destinations"] = "Destinations",
        ["groups"] = "ProfileGroups",
        ["templates"] = "QueryTemplates"
    };

    public ProfileDocument ToPublic(ProfileCatalog catalog, ProfileDocument document) => Rewrite(catalog, document, toPublic: true);

    // unknown ids stay as sent, so validation reports them on the field
    public ProfileDocument ToInternal(ProfileCatalog catalog, ProfileDocument document) => Rewrite(catalog, document, toPublic: false);

    public JsonObject ToPublic(JsonObject entity, string table)
    {
        foreach (var (key, value) in entity.ToList())
        {
            var target = key == "Id" ? table : PublicIdTables.ForName(key);
            if (target is not null && value is JsonValue number && number.GetValueKind() == JsonValueKind.Number)
                entity[key] = ids.ToUlid(target, long.Parse(number.ToJsonString()));
        }
        return entity;
    }

    private ProfileDocument Rewrite(ProfileCatalog catalog, ProfileDocument document, bool toPublic)
    {
        Rewrite(catalog.Settings, document.Settings, toPublic);
        foreach (var node in document.Nodes)
        {
            if (node.Config is { } config && catalog.Nodes.FirstOrDefault(n => n.Type == node.Type) is { } definition)
                Rewrite(definition.Fields, config, toPublic);
        }
        return document;
    }

    private void Rewrite(IEnumerable<NodeField> fields, JsonObject config, bool toPublic)
    {
        foreach (var field in fields)
        {
            var value = ConfigPath.Get(config, field.Key);
            if (field.Columns is { } columns && value is JsonArray rows)
            {
                foreach (var row in rows.OfType<JsonObject>())
                {
                    Rewrite(columns, row, toPublic);
                    if (field.Key == ProfileDocumentService.OutputTargetsKey && row["Id"] is JsonValue rowId)
                        row["Id"] = toPublic ? Public("ImportOutputTargets", rowId) : Internal("ImportOutputTargets", rowId);
                }
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
