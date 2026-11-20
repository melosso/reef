using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Dapper;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Reef.Tests.Api;

[Collection(ApiCollection.Name)]
public class EmbeddedIdApiTests(ReefApiFactory factory)
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private SqliteConnection Db() => new($"Data Source={Path.Combine(factory.DataFolder, "Reef.db")};Pooling=False");

    private Task<string> EmailProfileAsync(HttpClient admin, string attachmentConfig) =>
        EmailProfiles.CreateAsync(admin, factory.DataFolder, EmailProfiles.Smtp(1), "SELECT 'a@example.com' AS Email",
            new JsonObject { ["EmailAttachmentConfig"] = attachmentConfig });

    private static string PdfAttachment(string templateId) =>
        $$$"""{"Enabled":true,"Mode":"DocumentTemplate","DocumentTemplate":{"TemplateId":"{{{templateId}}}","GeneratePerRow":true}}""";

    [Fact]
    public async Task AttachmentTemplateTravelsAsUlidAndIsStoredAsId()
    {
        var admin = await factory.CreateAdminClientAsync();
        var pdfId = await EmailProfiles.IdOfAsync(await admin.PostAsJsonAsync("/api/templates", new { name = $"Pdf {Guid.NewGuid():N}", type = "DocumentTemplate", outputFormat = "PDF", template = "{{# content }}<p>x</p>{{/ content }}" }, _ct));

        var profileId = await EmailProfileAsync(admin, PdfAttachment(pdfId));

        await using var db = Db();
        var stored = await db.ExecuteScalarAsync<string>("SELECT EmailAttachmentConfig FROM Profiles WHERE Ulid = @profileId", new { profileId });
        var internalId = await db.ExecuteScalarAsync<long>("SELECT Id FROM QueryTemplates WHERE Ulid = @pdfId", new { pdfId });
        JsonNode.Parse(stored!)!["DocumentTemplate"]!["TemplateId"]!.GetValue<long>().Should().Be(internalId);

        var document = await admin.GetFromJsonAsync<JsonObject>($"/api/profile-documents/export/{profileId}", _ct);
        var mail = document!["nodes"]!.AsArray().First(n => n!["type"]!.GetValue<string>() == "emailexport")!;
        mail["config"]!["EmailAttachmentConfig"]!["DocumentTemplate"]!["TemplateId"]!.GetValue<string>().Should().Be(pdfId);

        var profile = await admin.GetFromJsonAsync<JsonObject>($"/api/profiles/{profileId}", _ct);
        JsonNode.Parse(profile!["emailAttachmentConfig"]!.GetValue<string>())!["DocumentTemplate"]!["TemplateId"]!.GetValue<string>().Should().Be(pdfId);
    }

    [Fact]
    public async Task UnreadableAttachmentConfigFailsTheRun()
    {
        var admin = await factory.CreateAdminClientAsync();
        var profileId = await EmailProfileAsync(admin, PdfAttachment("01ARZ3NDEKTSV4RRFFQ69G5FAV"));

        var latest = await EmailProfiles.RunAsync(admin, profileId);

        latest["status"]!.GetValue<string>().Should().Be("Failed");
        latest["errorMessage"]!.GetValue<string>().Should().ContainEquivalentOf("attachment configuration");
    }

    [Fact]
    public async Task JobDependenciesTravelAsUlids()
    {
        var admin = await factory.CreateAdminClientAsync();
        var first = await EmailProfiles.IdOfAsync(await admin.PostAsJsonAsync("/api/jobs", new { name = $"First {Guid.NewGuid():N}", type = "Cleanup", scheduleType = "Manual", isEnabled = true }, _ct));

        var second = await admin.PostAsJsonAsync("/api/jobs", new { name = $"Second {Guid.NewGuid():N}", type = "Cleanup", scheduleType = "Manual", isEnabled = true, dependsOnJobIds = first }, _ct);
        var secondId = await EmailProfiles.IdOfAsync(second);

        await using var db = Db();
        var firstInternal = await db.ExecuteScalarAsync<long>("SELECT Id FROM Jobs WHERE Ulid = @first", new { first });
        (await db.ExecuteScalarAsync<string>("SELECT DependsOnJobIds FROM Jobs WHERE Ulid = @secondId", new { secondId })).Should().Be(firstInternal.ToString());
        (await admin.GetFromJsonAsync<JsonObject>($"/api/jobs/{secondId}", _ct))!["dependsOnJobIds"]!.GetValue<string>().Should().Be(first);
    }

    [Fact]
    public async Task ProfileDependenciesTravelAsUlids()
    {
        var admin = await factory.CreateAdminClientAsync();
        var first = await EmailProfileAsync(admin, "");
        var second = await EmailProfileAsync(admin, "");
        await using (var db = Db())
            await db.ExecuteAsync("UPDATE Profiles SET DependsOnProfileIds = (SELECT Id FROM Profiles WHERE Ulid = @first) WHERE Ulid = @second", new { first, second });

        var profile = await admin.GetFromJsonAsync<JsonObject>($"/api/profiles/{second}", _ct);

        profile!["dependsOnProfileIds"]!.GetValue<string>().Should().Be(first);
    }
}
