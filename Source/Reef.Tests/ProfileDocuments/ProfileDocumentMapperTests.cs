using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Reef.Core.Models;
using Reef.Core.ProfileDocuments;
using Reef.Core.ProfileDocuments.Catalogs;
using Reef.Core.ProfileDocuments.Models;
using Xunit;

namespace Reef.Tests.ProfileDocuments;

public class ProfileDocumentMapperTests
{
    private static readonly ProfileCatalog Export = ProfileCatalogs.Find(ProfileKinds.Export)!;
    private static readonly ProfileCatalog Import = ProfileCatalogs.Find(ProfileKinds.Import)!;

    private static ProfileDocument Document(ProfileCatalog catalog, string name, params (string Type, JsonObject Config)[] nodes) => new()
    {
        Kind = catalog.Kind,
        Settings = new JsonObject { ["Name"] = name, ["IsEnabled"] = true },
        Nodes = nodes.Select(n => new ProfileDocumentNode { Id = n.Type, Type = n.Type, Config = n.Config }).ToList()
    };

    private static T Apply<T>(ProfileCatalog catalog, ProfileDocument document) =>
        ProfileDocumentMapper.Apply(catalog, document, ProfileCatalogs.Defaults(catalog.Kind), ProfileCatalogs.Defaults(catalog.Kind)).Deserialize<T>()!;

    [Fact]
    public void EmailExportDocumentMapsToProfileColumnsAndBack()
    {
        var document = Document(Export, "Order mails",
            ("source", new JsonObject { ["ConnectionId"] = 3, ["Query"] = "SELECT 1" }),
            ("preprocess", new JsonObject { ["PreProcessType"] = "Query", ["PreProcessConfig"] = new JsonObject { ["Command"] = "UPDATE x SET y = 1", ["Timeout"] = 45 } }),
            ("emailexport", new JsonObject { ["OutputDestinationId"] = 7, ["EmailTemplateId"] = 9, ["UseHardcodedRecipients"] = true, ["EmailRecipientsHardcoded"] = "ops@example.com" }));

        ProfileDocumentValidator.Validate(Export, document).Should().BeEmpty();
        var profile = Apply<Profile>(Export, document);

        profile.Name.Should().Be("Order mails");
        profile.IsEmailExport.Should().BeTrue();
        profile.OutputDestinationId.Should().Be(7);
        profile.PreProcessType.Should().Be("Query");
        var processing = JsonSerializer.Deserialize<ProcessingConfig>(profile.PreProcessConfig!)!;
        processing.Type.Should().Be("Query");
        processing.Timeout.Should().Be(45);

        var back = ProfileDocumentMapper.ToDocument(Export, JsonSerializer.SerializeToNode(profile)!.AsObject(), null, 1, "P-1");
        back.Nodes.Select(n => n.Type).Should().Equal("preprocess", "source", "emailexport");
        ConfigPath.Get(back.Nodes[0].Config, "PreProcessConfig.Command")!.GetValue<string>().Should().Be("UPDATE x SET y = 1");
    }

    [Fact]
    public void RemovingANodeResetsTheColumnsItOwns()
    {
        var existing = JsonSerializer.SerializeToNode(new Profile { Name = "x", Query = "q", Hash = "", DeltaSyncEnabled = true, DeltaSyncReefIdColumn = "Id", TemplateId = 4 })!.AsObject();
        var document = Document(Export, "x",
            ("source", new JsonObject { ["ConnectionId"] = 1, ["Query"] = "q" }),
            ("destination", new JsonObject { ["OutputDestinationId"] = 2, ["OutputFormat"] = "XML" }));

        var profile = ProfileDocumentMapper.Apply(Export, document, existing, ProfileCatalogs.ExportDefaults).Deserialize<Profile>()!;

        profile.DeltaSyncEnabled.Should().BeFalse();
        profile.DeltaSyncReefIdColumn.Should().BeNull();
        profile.TemplateId.Should().BeNull();
        profile.OutputFormat.Should().Be("XML");
    }

    [Fact]
    public void ValidatorReportsStructureAndFieldProblemsPerNode()
    {
        var document = Document(Export, "",
            ("source", new JsonObject { ["Query"] = "SELECT 1" }),
            ("splitoutput", new JsonObject { ["SplitKeyColumn"] = "Customer" }),
            ("emailexport", new JsonObject { ["UseHardcodedRecipients"] = true, ["EmailRecipientsHardcoded"] = "not-an-address" }));

        var errors = ProfileDocumentValidator.Validate(Export, document);

        errors.Should().Contain(e => e.Field == "Name" && e.NodeId == null);
        errors.Should().Contain(e => e.NodeId == "source" && e.Field == "ConnectionId");
        errors.Should().Contain(e => e.NodeId == "emailexport" && e.Field == "EmailRecipientsHardcoded" && e.Message.Contains("email"));
        errors.Should().Contain(e => e.NodeId == "splitoutput" && e.Message.Contains("Destination"));
        errors.Should().ContainSingle(e => e.Message.Contains("cannot be combined"));
    }

    [Fact]
    public void ImportDocumentKeepsStructuredColumnsJsonNative()
    {
        var mappings = new JsonArray(new JsonObject { ["sourceColumn"] = "order_id", ["targetColumn"] = "OrderId", ["isKeyColumn"] = true });
        var document = Document(Import, "Orders in",
            ("source", new JsonObject { ["SourceType"] = "Http", ["SourceConfig"] = new JsonObject { ["url"] = "https://api.example.com/orders" } }),
            ("parse", new JsonObject { ["SourceFormat"] = "JSONL" }),
            ("mapping", new JsonObject { ["ColumnMappingsJson"] = mappings }),
            ("target", new JsonObject { ["TargetType"] = "Database", ["TargetConnectionId"] = 2, ["TargetTable"] = "Orders", ["LoadStrategy"] = "Append" }));

        ProfileDocumentValidator.Validate(Import, document).Should().BeEmpty();
        var import = Apply<ImportProfile>(Import, document);

        import.FormatConfig.Should().Contain("\"isJsonLines\":true");
        JsonNode.Parse(import.SourceConfig!)!["url"]!.GetValue<string>().Should().Be("https://api.example.com/orders");

        var back = ProfileDocumentMapper.ToDocument(Import, JsonSerializer.SerializeToNode(import)!.AsObject(), null, 5, null);
        back.Nodes.Single(n => n.Type == "mapping").Config!["ColumnMappingsJson"].Should().BeOfType<JsonArray>();
    }
}
