using System.Text.Json;
using System.Text.Json.Nodes;
using Reef.Core.Models;
using Reef.Core.ProfileDocuments;
using Reef.Core.ProfileDocuments.Catalogs;
using Reef.Core.ProfileDocuments.Models;

namespace Reef.Core.Workflows.Engine;

// Import sources and targets read an ImportProfile
public static class ImportNodeProfile
{
    public static ImportProfile From(string step, JsonObject config, string workflowName)
    {
        var document = new ProfileDocument
        {
            Kind = ProfileKinds.Import,
            Settings = new JsonObject { ["Name"] = workflowName },
            Nodes = [new ProfileDocumentNode { Id = step, Type = step, Config = config.DeepClone().AsObject() }]
        };
        var entity = ProfileDocumentMapper.Apply(ProfileCatalogs.Find(ProfileKinds.Import)!, document, ProfileCatalogs.ImportDefaults, ProfileCatalogs.ImportDefaults);
        return entity.Deserialize<ImportProfile>()!;
    }
}
