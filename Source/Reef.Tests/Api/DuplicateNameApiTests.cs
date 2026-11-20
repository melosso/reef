using System.Net;
using System.Net.Http.Json;
using FluentAssertions;

namespace Reef.Tests.Api;

[Collection(ApiCollection.Name)]
public class DuplicateNameApiTests(ReefApiFactory factory)
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    public static TheoryData<string, string> Entities => new()
    {
        { "/api/connections", """{"name":"{name}","type":"Sqlite","connectionString":"Data Source=dup.db","isActive":true,"hash":""}""" },
        { "/api/destinations", """{"name":"{name}","type":"Local","configurationJson":"{\"BasePath\":\"exports\"}"}""" },
        { "/api/templates", """{"name":"{name}","type":"ScribanTemplate","outputFormat":"JSON","template":"{}"}""" },
        { "/api/jobs", """{"name":"{name}","type":"Cleanup","scheduleType":"Manual","isEnabled":true}""" },
        { "/api/groups", """{"name":"{name}","description":""}""" }
    };

    [Theory]
    [MemberData(nameof(Entities))]
    public async Task DuplicateNameReturnsConflict(string path, string body)
    {
        var admin = await factory.CreateAdminClientAsync();
        var json = body.Replace("{name}", $"Duplicate {Guid.NewGuid():N}");
        HttpContent Content() => new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        (await admin.PostAsync(path, Content(), _ct)).IsSuccessStatusCode.Should().BeTrue();

        var second = await admin.PostAsync(path, Content(), _ct);

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await second.Content.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>(_ct))!["message"]!.GetValue<string>().Should().Contain("already exists");
    }

    [Fact]
    public async Task DuplicateProfileNameReturnsNameFieldError()
    {
        var admin = await factory.CreateAdminClientAsync();
        var destinations = await admin.GetFromJsonAsync<System.Text.Json.Nodes.JsonArray>("/api/destinations", _ct);
        var destinationId = destinations!.First(d => d!["type"]!.GetValue<string>() == "Local")!["id"]!.GetValue<string>();
        var connectionId = await EmailProfiles.IdOfAsync(await admin.PostAsJsonAsync("/api/connections", new { name = $"Dup {Guid.NewGuid():N}", type = "Sqlite", connectionString = "Data Source=dup.db", isActive = true, hash = "" }, _ct));
        var name = $"Duplicate {Guid.NewGuid():N}";
        HttpContent Document() => JsonContent.Create(new System.Text.Json.Nodes.JsonObject
        {
            ["kind"] = "export",
            ["settings"] = new System.Text.Json.Nodes.JsonObject { ["Name"] = name, ["IsEnabled"] = true },
            ["nodes"] = new System.Text.Json.Nodes.JsonArray(
                new System.Text.Json.Nodes.JsonObject { ["id"] = "source", ["type"] = "source", ["x"] = 0, ["y"] = 0, ["config"] = new System.Text.Json.Nodes.JsonObject { ["ConnectionId"] = connectionId, ["Query"] = "SELECT 1 AS One" } },
                new System.Text.Json.Nodes.JsonObject { ["id"] = "destination", ["type"] = "destination", ["x"] = 300, ["y"] = 0, ["config"] = new System.Text.Json.Nodes.JsonObject { ["OutputDestinationId"] = destinationId, ["OutputFormat"] = "JSON" } })
        });
        (await admin.PostAsync("/api/profile-documents/export", Document(), _ct)).EnsureSuccessStatusCode();

        var second = await admin.PostAsync("/api/profile-documents/export", Document(), _ct);

        second.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await second.Content.ReadAsStringAsync(_ct)).Should().Contain("already exists").And.Contain("Name");
    }
}
