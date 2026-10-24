using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Xunit;

namespace Reef.Tests.Api;

[Collection(ApiCollection.Name)]
public class ApiKeyAuthTests(ReefApiFactory factory)
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private async Task<(string Id, string Key)> CreateKeyAsync(HttpClient admin, bool write)
    {
        var created = await admin.PostAsJsonAsync("/api/admin/api-keys", new { name = $"key {Guid.NewGuid():N}", permissions = $$"""{"read":true,"write":{{(write ? "true" : "false")}}}""" }, _ct);
        var body = (await created.Content.ReadFromJsonAsync<JsonObject>(_ct))!;
        return (body["id"]!.GetValue<string>(), body["apiKey"]!.GetValue<string>());
    }

    private HttpClient KeyClient(string key)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", key);
        return client;
    }

    [Fact]
    public async Task ApiKeyAuthenticatesUntilRevoked()
    {
        var admin = await factory.CreateAdminClientAsync();
        var (id, key) = await CreateKeyAsync(admin, write: true);

        (await KeyClient(key).GetAsync("/api/profiles", _ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await admin.DeleteAsync($"/api/admin/api-keys/{id}", _ct)).EnsureSuccessStatusCode();
        (await KeyClient(key).GetAsync("/api/profiles", _ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ReadOnlyKeyCannotWrite()
    {
        var (_, key) = await CreateKeyAsync(await factory.CreateAdminClientAsync(), write: false);

        var write = await KeyClient(key).PostAsJsonAsync("/api/connections", new { name = "x", type = "Sqlite", connectionString = "Data Source=x.db", isActive = true, hash = "" }, _ct);

        write.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ApiKeyCannotReachAdminEndpoints()
    {
        var (_, key) = await CreateKeyAsync(await factory.CreateAdminClientAsync(), write: true);

        (await KeyClient(key).GetAsync("/api/admin/api-keys", _ct)).IsSuccessStatusCode.Should().BeFalse();
    }

    [Fact]
    public async Task UnknownKeyIsRejected()
    {
        (await KeyClient("reef_ak_00000000_nope").GetAsync("/api/profiles", _ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
