using System.Text.Json;
using System.Text.Json.Nodes;
using Reef.Core.Nodes;
using Reef.Core.Nodes.Models;
using Reef.Core.ProfileDocuments.Models;
using Reef.Core.PublicIds;

namespace Reef.Core.ProfileDocuments;

public sealed class ProfileDocumentIds(PublicIdMap ids, NodeFieldIds fieldIds)
{
    public ProfileDocument ToPublic(ProfileCatalog catalog, ProfileDocument document) => Rewrite(catalog, document, toPublic: true);

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
        fieldIds.Rewrite(fields, config, toPublic);
        foreach (var field in fields.Where(f => f.Key == ProfileDocumentService.OutputTargetsKey))
        {
            foreach (var row in (ConfigPath.Get(config, field.Key) as JsonArray ?? []).OfType<JsonObject>())
            {
                if (row["Id"] is JsonValue rowId)
                    row["Id"] = toPublic ? Public("ImportOutputTargets", rowId) : Internal("ImportOutputTargets", rowId);
            }
        }
    }

    private JsonNode? Public(string table, JsonValue value) => value.GetValueKind() != JsonValueKind.Number
        ? value.DeepClone()
        : ids.ToUlid(table, long.Parse(value.ToJsonString())) is { } ulid ? JsonValue.Create(ulid) : null;

    private JsonNode Internal(string table, JsonValue value) =>
        value.GetValueKind() == JsonValueKind.String && ids.TryToId(table, value.GetValue<string>(), out var id) ? JsonValue.Create(id) : value.DeepClone();
}
