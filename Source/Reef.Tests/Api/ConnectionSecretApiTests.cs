using System.Text.Json.Nodes;
using Dapper;
using FluentAssertions;
using Microsoft.Data.Sqlite;

namespace Reef.Tests.Api;

[Collection(ApiCollection.Name)]
public class ConnectionSecretApiTests(ReefApiFactory factory)
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private async Task<(HttpClient Admin, string Id, string Name)> CreateAsync()
    {
        var admin = await factory.CreateAdminClientAsync();
        var name = $"Secret {Guid.NewGuid():N}";
        var shopDb = Path.Combine(factory.DataFolder, $"secret-{Guid.NewGuid():N}.db");
        var id = await EmailProfiles.IdOfAsync(await admin.PostAsJsonAsync("/api/connections", new { name, type = "Sqlite", connectionString = $"Data Source={shopDb}", isActive = true, hash = "" }, _ct));
        return (admin, id, name);
    }

    [Fact]
    public async Task ResponsesNeverIncludeTheStoredConnectionString()
    {
        var (admin, id, _) = await CreateAsync();

        var single = await admin.GetFromJsonAsync<JsonObject>($"/api/connections/{id}", _ct);
        var all = await admin.GetFromJsonAsync<JsonArray>("/api/connections", _ct);

        single!["connectionString"]!.GetValue<string>().Should().BeEmpty();
        all!.Select(c => c!["connectionString"]!.GetValue<string>()).Should().OnlyContain(s => s.Length == 0);
    }

    [Fact]
    public async Task EmptyConnectionStringOnUpdateRetainsTheStoredValue()
    {
        var (admin, id, name) = await CreateAsync();
        string? before;
        await using (var db = new SqliteConnection($"Data Source={Path.Combine(factory.DataFolder, "Reef.db")};Pooling=False"))
            before = await db.ExecuteScalarAsync<string>("SELECT ConnectionString FROM Connections WHERE Ulid = @id", new { id });

        (await admin.PutAsJsonAsync($"/api/connections/{id}", new { name = name + " renamed", type = "Sqlite", connectionString = "", isActive = true, hash = "" }, _ct)).EnsureSuccessStatusCode();

        await using (var db = new SqliteConnection($"Data Source={Path.Combine(factory.DataFolder, "Reef.db")};Pooling=False"))
            (await db.ExecuteScalarAsync<string>("SELECT ConnectionString FROM Connections WHERE Ulid = @id", new { id })).Should().Be(before);
        var test = await admin.PostAsJsonAsync("/api/connections/test", new { type = "Sqlite", connectionString = "", connectionId = id }, _ct);
        (await test.Content.ReadFromJsonAsync<JsonObject>(_ct))!["success"]!.GetValue<bool>().Should().BeTrue();
    }
}
