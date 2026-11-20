using System.Text.Json;
using System.Text.Json.Nodes;
using Reef.Core.ProfileDocuments;

namespace Reef.Core.PublicIds;

// Rewrites ids held inside text fields, comma lists and json configs, which the typed id mapping cannot reach.
public sealed class EmbeddedIds(PublicIdMap ids)
{
    private static readonly Dictionary<string, string> Lists = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DependsOnProfileIds"] = PublicIdTables.Profiles,
        ["DependsOnJobIds"] = "Jobs"
    };

    private static readonly Dictionary<string, (string Path, string Table)[]> Configs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["EmailAttachmentConfig"] = [("DocumentTemplate.TemplateId", "QueryTemplates")]
    };

    public static bool Handles(string property) => Lists.ContainsKey(property) || Configs.ContainsKey(property);

    // unknown ids are dropped on the way out, so a deleted row never surfaces as an integer
    public string? ToPublic(string property, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        if (Lists.TryGetValue(property, out var table))
            return string.Join(",", Split(text).Select(part => long.TryParse(part, out var id) ? ids.ToUlid(table, id) : null).OfType<string>());
        return RewriteConfig(property, text, toPublic: true);
    }

    // returns null when a list names an unknown id, configs keep unknown values so the run reports them
    public string? ToInternal(string property, string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        if (Lists.TryGetValue(property, out var table))
        {
            var mapped = Split(text).Select(part => ids.TryToId(table, part, out var id) ? id.ToString() : null).ToList();
            return mapped.Contains(null) ? null : string.Join(",", mapped);
        }
        return RewriteConfig(property, text, toPublic: false);
    }

    public void Rewrite(string property, JsonObject config, bool toPublic)
    {
        if (!Configs.TryGetValue(property, out var paths)) return;
        foreach (var (path, table) in paths)
        {
            if (ConfigPath.Get(config, path) is JsonValue value)
                ConfigPath.Set(config, path, toPublic ? Public(table, value) : Internal(table, value));
        }
    }

    private JsonNode Public(string table, JsonValue value) =>
        value.GetValueKind() == JsonValueKind.Number && ids.ToUlid(table, value.GetValue<long>()) is { } ulid ? JsonValue.Create(ulid) : value.DeepClone();

    private JsonNode Internal(string table, JsonValue value) =>
        value.GetValueKind() == JsonValueKind.String && ids.TryToId(table, value.GetValue<string>(), out var id) ? JsonValue.Create(id) : value.DeepClone();

    private static string[] Split(string text) => text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private string RewriteConfig(string property, string text, bool toPublic)
    {
        if (!Configs.ContainsKey(property)) return text;
        JsonObject? root;
        try { root = JsonNode.Parse(text) as JsonObject; }
        catch (JsonException) { return text; }
        if (root is null) return text;
        Rewrite(property, root, toPublic);
        return root.ToJsonString();
    }
}
