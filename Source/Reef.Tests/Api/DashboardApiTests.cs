using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Reef.Tests.Api;

[Collection(ApiCollection.Name)]
public class DashboardApiTests(ReefApiFactory factory)
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private async Task ExecAsync(string database, string sql)
    {
        await using var db = new SqliteConnection($"Data Source={database}");
        await db.OpenAsync(_ct);
        await using var cmd = db.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(_ct);
    }

    private async Task<int> CreateProfileAsync(HttpClient client, string shopDb, string query)
    {
        var connection = await client.PostAsJsonAsync("/api/connections", new { name = $"Shop {Guid.NewGuid():N}", type = "Sqlite", connectionString = $"Data Source={shopDb}", isActive = true, hash = "" }, _ct);
        connection.EnsureSuccessStatusCode();
        var connectionId = (await connection.Content.ReadFromJsonAsync<JsonObject>(_ct))!["id"]!.GetValue<int>();
        var destinations = await client.GetFromJsonAsync<JsonArray>("/api/destinations", _ct);
        var destinationId = destinations!.First(d => d!["type"]!.GetValue<string>() == "Local")!["id"]!.GetValue<int>();
        var document = new JsonObject
        {
            ["kind"] = "export",
            ["settings"] = new JsonObject { ["Name"] = $"Dashboard {Guid.NewGuid():N}", ["IsEnabled"] = true },
            ["nodes"] = new JsonArray(
                new JsonObject { ["id"] = "source", ["type"] = "source", ["x"] = 0, ["y"] = 0, ["config"] = new JsonObject { ["ConnectionId"] = connectionId, ["Query"] = query } },
                new JsonObject { ["id"] = "destination", ["type"] = "destination", ["x"] = 300, ["y"] = 0, ["config"] = new JsonObject { ["OutputDestinationId"] = destinationId, ["OutputFormat"] = "JSON" } })
        };
        var created = await client.PostAsJsonAsync("/api/profile-documents/export", document, _ct);
        created.EnsureSuccessStatusCode();
        return (await created.Content.ReadFromJsonAsync<JsonObject>(_ct))!["id"]!.GetValue<int>();
    }

    private async Task RunAsync(HttpClient client, int profileId) =>
        await client.PostAsJsonAsync($"/api/executions/execute/{profileId}", new { }, _ct);

    private async Task<HttpClient> CreateUserClientAsync(HttpClient admin)
    {
        var name = $"dash{Guid.NewGuid():N}"[..16];
        (await admin.PostAsJsonAsync("/api/admin/users", new { username = name, password = "Secret-Pass-123", role = "User" }, _ct)).EnsureSuccessStatusCode();
        var login = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { username = name, password = "Secret-Pass-123" }, _ct);
        var user = factory.CreateClient();
        user.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await login.Content.ReadFromJsonAsync<JsonObject>(_ct))!["token"]!.GetValue<string>());
        return user;
    }

    private static IEnumerable<int> ProfileIds(JsonObject attention, string section) =>
        attention[section]!.AsArray().Select(i => i!["profileId"]!.GetValue<int>());

    [Fact]
    public async Task Attention_lists_failed_profiles_and_row_count_drops_for_any_signed_in_user()
    {
        var admin = await factory.CreateAdminClientAsync();
        var shopDb = Path.Combine(factory.DataFolder, $"dash-{Guid.NewGuid():N}.db");
        await ExecAsync(shopDb, "CREATE TABLE Orders (OrderId INTEGER PRIMARY KEY); INSERT INTO Orders VALUES (1), (2), (3), (4);");

        var failing = await CreateProfileAsync(admin, shopDb, "SELECT * FROM MissingTable");
        await RunAsync(admin, failing);

        var dropping = await CreateProfileAsync(admin, shopDb, "SELECT * FROM Orders");
        var delta = await CreateProfileAsync(admin, shopDb, "SELECT * FROM Orders");
        for (var i = 0; i < 3; i++)
        {
            await RunAsync(admin, dropping);
            await RunAsync(admin, delta);
        }
        await ExecAsync(shopDb, "DELETE FROM Orders;");
        await RunAsync(admin, dropping);
        await RunAsync(admin, delta);
        await ExecAsync(Path.Combine(factory.DataFolder, "Reef.db"), $"UPDATE Profiles SET DeltaSyncEnabled = 1 WHERE Id = {delta};");

        var user = await CreateUserClientAsync(admin);
        var response = await user.GetAsync("/api/dashboard/attention", _ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var attention = (await response.Content.ReadFromJsonAsync<JsonObject>(_ct))!;
        ProfileIds(attention, "failed").Should().Contain(failing).And.NotContain(dropping);
        ProfileIds(attention, "anomalies").Should().Contain(dropping).And.NotContain(delta);
        attention["anomalies"]!.AsArray().First(a => a!["profileId"]!.GetValue<int>() == dropping)!["reason"]!.GetValue<string>().Should().Be("rows");
        attention["last24h"]!["failed"]!.GetValue<int>().Should().BeGreaterThan(0);
        attention["upNext"].Should().NotBeNull();
    }

    [Fact]
    public async Task Failed_profile_leaves_attention_after_a_successful_run()
    {
        var admin = await factory.CreateAdminClientAsync();
        var shopDb = Path.Combine(factory.DataFolder, $"dash-{Guid.NewGuid():N}.db");
        var profile = await CreateProfileAsync(admin, shopDb, "SELECT * FROM Orders");
        await RunAsync(admin, profile);

        ProfileIds((await admin.GetFromJsonAsync<JsonObject>("/api/dashboard/attention", _ct))!, "failed").Should().Contain(profile);

        await ExecAsync(shopDb, "CREATE TABLE Orders (OrderId INTEGER PRIMARY KEY); INSERT INTO Orders VALUES (1);");
        await RunAsync(admin, profile);

        ProfileIds((await admin.GetFromJsonAsync<JsonObject>("/api/dashboard/attention", _ct))!, "failed").Should().NotContain(profile);
    }
}
