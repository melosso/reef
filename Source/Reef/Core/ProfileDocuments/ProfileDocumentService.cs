using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Reef.Core.Models;
using Reef.Core.ProfileDocuments.Catalogs;
using Reef.Core.ProfileDocuments.Exceptions;
using Reef.Core.ProfileDocuments.Models;
using Reef.Core.Services;

namespace Reef.Core.ProfileDocuments;

public class ProfileDocumentService(ProfileService profiles, ImportProfileService imports)
{
    // ImportOutputTargets rows, exposed to the document as a pseudo column.
    public const string OutputTargetsKey = "OutputTargets";

    public ProfileDocument New(ProfileCatalog catalog) =>
        ProfileDocumentMapper.ToDocument(catalog, ProfileCatalogs.Defaults(catalog.Kind), null, null, null);

    public async Task<ProfileDocument?> GetAsync(ProfileCatalog catalog, int id, CancellationToken ct = default)
    {
        if (catalog.Kind == ProfileKinds.Import)
        {
            var import = await imports.GetByIdAsync(id);
            if (import is null)
                return null;
            var entity = Serialize<ImportProfile>(import);
            entity[OutputTargetsKey] = await ReadOutputTargetsAsync(id);
            return ProfileDocumentMapper.ToDocument(catalog, entity, ReadLayout(import.CanvasLayoutJson), import.Id, import.Code);
        }

        var profile = await profiles.GetByIdAsync(id, ct);
        return profile is null ? null : ProfileDocumentMapper.ToDocument(catalog, Serialize<Profile>(profile), ReadLayout(profile.CanvasLayoutJson), profile.Id, profile.Code);
    }

    // Resolves a document to profile columns for test and preview endpoints.
    public async Task<JsonObject> FlattenAsync(ProfileCatalog catalog, int? id, ProfileDocument document, CancellationToken ct = default)
    {
        var defaults = ProfileCatalogs.Defaults(catalog.Kind);
        var existing = id is null ? defaults
            : catalog.Kind == ProfileKinds.Import
                ? await LoadAsync<ImportProfile>(async () => await imports.GetByIdAsync(id.Value))
                : await LoadAsync<Profile>(async () => await profiles.GetByIdAsync(id.Value, ct));
        return ProfileDocumentMapper.Apply(catalog, document, existing, defaults);
    }

    public async Task<ProfileDocument> SaveAsync(ProfileCatalog catalog, int? id, ProfileDocument document, int? userId, CancellationToken ct = default)
    {
        var errors = ProfileDocumentValidator.Validate(catalog, document);
        if (errors.Count > 0)
            throw new ProfileDocumentValidationException(errors);

        var name = ConfigPath.ToText(document.Settings["Name"]).Trim();
        if (catalog.Kind == ProfileKinds.Import && (await imports.GetAllAsync()).Any(p => p.Id != id && string.Equals(p.Name.Trim(), name, StringComparison.OrdinalIgnoreCase)))
            throw DuplicateName(name);

        var defaults = ProfileCatalogs.Defaults(catalog.Kind);
        int savedId;
        try
        {
            savedId = await PersistAsync(catalog, id, document, defaults, userId, ct);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19 && ex.Message.Contains(".Name"))
        {
            throw DuplicateName(name);
        }

        return await GetAsync(catalog, savedId, ct) ?? throw new InvalidOperationException("Profile was saved but could not be read back");
    }

    private async Task<int> PersistAsync(ProfileCatalog catalog, int? id, ProfileDocument document, JsonObject defaults, int? userId, CancellationToken ct)
    {
        if (catalog.Kind == ProfileKinds.Import)
        {
            var existing = id is null ? defaults : await LoadAsync<ImportProfile>(async () => await imports.GetByIdAsync(id.Value));
            var applied = ProfileDocumentMapper.Apply(catalog, document, existing, defaults);
            var import = Deserialize<ImportProfile>(applied);
            import.Hash = "";
            import.CanvasLayoutJson = JsonSerializer.Serialize(ProfileDocumentMapper.ToLayout(document));
            if (id is null)
                import.Id = await imports.CreateAsync(import, userId);
            else
            {
                import.Id = id.Value;
                await imports.UpdateAsync(import);
            }
            await SyncOutputTargetsAsync(import.Id, ConfigPath.ParseAny(applied[OutputTargetsKey]) as JsonArray);
            return import.Id;
        }
        else
        {
            var existing = id is null ? defaults : await LoadAsync<Profile>(async () => await profiles.GetByIdAsync(id.Value, ct));
            var profile = Deserialize<Profile>(ProfileDocumentMapper.Apply(catalog, document, existing, defaults));
            profile.CanvasLayoutJson = JsonSerializer.Serialize(ProfileDocumentMapper.ToLayout(document));
            if (id is null)
                return await profiles.CreateAsync(profile, userId, ct);
            profile.Id = id.Value;
            await profiles.UpdateAsync(profile, ct);
            return id.Value;
        }
    }

    private async Task<JsonArray?> ReadOutputTargetsAsync(int importProfileId)
    {
        var targets = await imports.GetOutputTargetsAsync(importProfileId);
        if (targets.Count == 0)
            return null;
        return new JsonArray(targets.OrderBy(t => t.SortOrder).Select(t => (JsonNode)new JsonObject
        {
            ["Id"] = t.Id,
            ["DestinationId"] = t.DestinationId,
            ["OutputFormat"] = t.OutputFormat,
            ["FilenameTemplate"] = t.FilenameTemplate,
            ["OnFailure"] = t.OnFailure,
            ["IsEnabled"] = t.IsEnabled
        }).ToArray());
    }

    private async Task SyncOutputTargetsAsync(int importProfileId, JsonArray? rows)
    {
        var existing = (await imports.GetOutputTargetsAsync(importProfileId)).ToDictionary(t => t.Id);
        var kept = new HashSet<int>();
        var order = 0;

        foreach (var row in rows?.OfType<JsonObject>() ?? [])
        {
            var id = row["Id"]?.GetValueKind() == JsonValueKind.Number ? row["Id"]!.GetValue<int>() : 0;
            var target = existing.GetValueOrDefault(id) ?? new ImportOutputTarget { ImportProfileId = importProfileId };
            target.DestinationId = row["DestinationId"]?.GetValue<int>() ?? 0;
            target.OutputFormat = ConfigPath.ToText(row["OutputFormat"]) is { Length: > 0 } format ? format : "JSON";
            target.FilenameTemplate = ConfigPath.ToText(row["FilenameTemplate"]) is { Length: > 0 } name ? name : null;
            target.OnFailure = ConfigPath.ToText(row["OnFailure"]) is { Length: > 0 } onFailure ? onFailure : "Warn";
            target.IsEnabled = row["IsEnabled"]?.GetValueKind() != JsonValueKind.False;
            target.SortOrder = order++;

            if (existing.ContainsKey(target.Id))
            {
                kept.Add(target.Id);
                await imports.UpdateOutputTargetAsync(target);
            }
            else
                await imports.AddOutputTargetAsync(target);
        }

        foreach (var stale in existing.Keys.Where(k => !kept.Contains(k)))
            await imports.DeleteOutputTargetAsync(stale);
    }

    private static ProfileDocumentValidationException DuplicateName(string name) =>
        new([new($"A profile named '{name}' already exists", null, "Name")]);

    private static async Task<JsonObject> LoadAsync<T>(Func<Task<T?>> load) where T : class =>
        Serialize<T>(await load() ?? throw new KeyNotFoundException("Profile not found"));

    private static JsonObject Serialize<T>(T value) => JsonSerializer.SerializeToNode(value)!.AsObject();

    private static T Deserialize<T>(JsonObject json)
    {
        try
        {
            return json.Deserialize<T>() ?? throw new InvalidOperationException("Profile document is empty");
        }
        catch (JsonException ex)
        {
            throw new ProfileDocumentValidationException([new($"A value has the wrong type: {ex.Path}")]);
        }
    }

    private static ProfileDocumentLayout? ReadLayout(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<ProfileDocumentLayout>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
