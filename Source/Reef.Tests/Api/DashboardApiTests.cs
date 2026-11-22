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

    private async Task<string> CreateWorkflowAsync(HttpClient client, string shopDb, string query, bool smartSync = false)
    {
        var connection = await client.PostAsJsonAsync("/api/connections", new { name = $"Shop {Guid.NewGuid():N}", type = "Sqlite", connectionString = $"Data Source={shopDb}", isActive = true, hash = "" }, _ct);
        connection.EnsureSuccessStatusCode();
        var connectionId = (await connection.Content.ReadFromJsonAsync<JsonObject>(_ct))!["id"]!.GetValue<string>();
        var query1 = TestWorkflows.Node("query", "query", new JsonObject { ["ConnectionId"] = connectionId, ["Query"] = query });
        return smartSync
            ? await TestWorkflows.CreateChainAsync(client, query1, TestWorkflows.Node("sync", "deltasync", new JsonObject { ["IdColumn"] = "OrderId" }))
            : await TestWorkflows.CreateChainAsync(client, query1);
    }

    private async Task RunAsync(HttpClient client, string workflowId) =>
        await client.PostAsync($"/api/workflows/{workflowId}/run", null, _ct);

    private async Task<HttpClient> CreateUserClientAsync(HttpClient admin)
    {
        var name = $"dash{Guid.NewGuid():N}"[..16];
        (await admin.PostAsJsonAsync("/api/admin/users", new { username = name, password = "Secret-Pass-123", role = "User" }, _ct)).EnsureSuccessStatusCode();
        var login = await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { username = name, password = "Secret-Pass-123" }, _ct);
        var user = factory.CreateClient();
        user.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await login.Content.ReadFromJsonAsync<JsonObject>(_ct))!["token"]!.GetValue<string>());
        return user;
    }

    private static IEnumerable<string> WorkflowIds(JsonObject attention, string section) =>
        attention[section]!.AsArray().Select(i => i!["workflowId"]!.GetValue<string>());

    [Fact]
    public async Task AttentionListsFailedWorkflowsAndRowCountDropsForAnySignedInUser()
    {
        var admin = await factory.CreateAdminClientAsync();
        var shopDb = Path.Combine(factory.DataFolder, $"dash-{Guid.NewGuid():N}.db");
        await ExecAsync(shopDb, "CREATE TABLE Orders (OrderId INTEGER PRIMARY KEY); INSERT INTO Orders VALUES (1), (2), (3), (4);");

        var failing = await CreateWorkflowAsync(admin, shopDb, "SELECT * FROM MissingTable");
        await RunAsync(admin, failing);

        var dropping = await CreateWorkflowAsync(admin, shopDb, "SELECT * FROM Orders");
        var delta = await CreateWorkflowAsync(admin, shopDb, "SELECT * FROM Orders", smartSync: true);
        for (var i = 0; i < 3; i++)
        {
            await RunAsync(admin, dropping);
            await RunAsync(admin, delta);
        }
        await ExecAsync(shopDb, "DELETE FROM Orders;");
        await RunAsync(admin, dropping);
        await RunAsync(admin, delta);

        var user = await CreateUserClientAsync(admin);
        var response = await user.GetAsync("/api/dashboard/attention", _ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var attention = (await response.Content.ReadFromJsonAsync<JsonObject>(_ct))!;
        WorkflowIds(attention, "failed").Should().Contain(failing).And.NotContain(dropping);
        WorkflowIds(attention, "anomalies").Should().Contain(dropping).And.NotContain(delta);
        attention["anomalies"]!.AsArray().First(a => a!["workflowId"]!.GetValue<string>() == dropping)!["reason"]!.GetValue<string>().Should().Be("rows");
        attention["last24h"]!["failed"]!.GetValue<int>().Should().BeGreaterThan(0);
        attention["upNext"].Should().NotBeNull();
    }

    [Fact]
    public async Task FailedWorkflowLeavesAttentionAfterASuccessfulRun()
    {
        var admin = await factory.CreateAdminClientAsync();
        var shopDb = Path.Combine(factory.DataFolder, $"dash-{Guid.NewGuid():N}.db");
        var workflow = await CreateWorkflowAsync(admin, shopDb, "SELECT * FROM Orders");
        await RunAsync(admin, workflow);

        WorkflowIds((await admin.GetFromJsonAsync<JsonObject>("/api/dashboard/attention", _ct))!, "failed").Should().Contain(workflow);

        await ExecAsync(shopDb, "CREATE TABLE Orders (OrderId INTEGER PRIMARY KEY); INSERT INTO Orders VALUES (1);");
        await RunAsync(admin, workflow);

        WorkflowIds((await admin.GetFromJsonAsync<JsonObject>("/api/dashboard/attention", _ct))!, "failed").Should().NotContain(workflow);
    }
}
