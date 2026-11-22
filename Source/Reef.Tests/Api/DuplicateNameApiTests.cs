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
    public async Task JobWithInvalidRetriesIsRejected()
    {
        var admin = await factory.CreateAdminClientAsync();

        var response = await admin.PostAsJsonAsync("/api/jobs", new { name = $"Retry {Guid.NewGuid():N}", type = "Cleanup", scheduleType = "Manual", maxRetries = 50, isEnabled = true }, _ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(_ct)).Should().Contain("Retries");
    }
}
