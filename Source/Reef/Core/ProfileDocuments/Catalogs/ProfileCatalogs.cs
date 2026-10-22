using System.Text.Json;
using System.Text.Json.Nodes;
using Reef.Core.Models;
using Reef.Core.ProfileDocuments.Models;

namespace Reef.Core.ProfileDocuments.Catalogs;

public static class ProfileCatalogs
{
    public static readonly JsonObject ExportDefaults = JsonSerializer.SerializeToNode(new Profile { Name = "", Query = "", Hash = "" })!.AsObject();
    public static readonly JsonObject ImportDefaults = JsonSerializer.SerializeToNode(new ImportProfile { Name = "", TargetTable = "", Hash = "" })!.AsObject();

    private static readonly ProfileCatalog Export = WithDefaults(ExportProfileCatalog.Build(), ExportDefaults);
    private static readonly ProfileCatalog Import = WithDefaults(ImportProfileCatalog.Build(), ImportDefaults);

    public static ProfileCatalog? Find(string kind) => kind.ToLowerInvariant() switch
    {
        ProfileKinds.Export => Export,
        ProfileKinds.Import => Import,
        _ => null
    };

    public static JsonObject Defaults(string kind) => kind == ProfileKinds.Import ? ImportDefaults : ExportDefaults;

    private static ProfileCatalog WithDefaults(ProfileCatalog catalog, JsonObject defaults)
    {
        foreach (var field in catalog.Settings.Concat(catalog.Nodes.SelectMany(n => n.Fields)))
            field.Default ??= EntityFields.Read(defaults, field);
        return catalog;
    }
}
