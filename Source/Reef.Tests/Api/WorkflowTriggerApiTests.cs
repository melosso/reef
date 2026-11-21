using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.Sqlite;
using Reef.Core.Workflows.Triggers;
using Reef.Tests.Templates;
using Xunit;

namespace Reef.Tests.Api;

[Collection(ApiCollection.Name)]
public class WorkflowTriggerApiTests(ReefApiFactory factory)
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private static JsonObject Node(string id, string type, JsonObject? config = null) =>
        new() { ["id"] = id, ["type"] = type, ["x"] = 0, ["y"] = 0, ["config"] = config ?? new JsonObject() };

    private async Task<string> CreateAsync(HttpClient client, bool active, JsonArray nodes, JsonArray? edges = null)
    {
        var created = await client.PostAsJsonAsync("/api/workflows", new JsonObject
        {
            ["name"] = $"Trigger {Guid.NewGuid():N}", ["active"] = active, ["nodes"] = nodes, ["edges"] = edges ?? []
        }, _ct);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(_ct));
        return (await created.Content.ReadFromJsonAsync<JsonObject>(_ct))!["id"]!.GetValue<string>();
    }

    private async Task<JsonArray> ExecutionsAsync(HttpClient client, string id) =>
        (await client.GetFromJsonAsync<JsonArray>($"/api/workflows/{id}/executions", _ct))!;

    private async Task RunDueAsync(DateTime now)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<WorkflowScheduler>().RunDueAsync(now, _ct);
    }

    [Fact]
    public async Task ScheduleTriggerRunsActiveWorkflowsWhenDue()
    {
        var client = await factory.CreateAdminClientAsync();
        JsonArray Nodes() => [Node("tick", "schedule", new JsonObject { ["Repeat"] = "Interval", ["EveryMinutes"] = 5 })];
        var active = await CreateAsync(client, true, Nodes());
        var inactive = await CreateAsync(client, false, Nodes());

        await RunDueAsync(DateTime.UtcNow.AddMinutes(1));
        (await ExecutionsAsync(client, active)).Should().BeEmpty();

        await RunDueAsync(DateTime.UtcNow.AddMinutes(6));
        var runs = await ExecutionsAsync(client, active);
        runs.Should().ContainSingle().Which!["triggerNodeId"]!.GetValue<string>().Should().Be("tick");
        (await ExecutionsAsync(client, inactive)).Should().BeEmpty();

        await RunDueAsync(DateTime.UtcNow.AddMinutes(1));
        (await ExecutionsAsync(client, active)).Should().ContainSingle();
    }

    [Fact]
    public async Task InvalidCronIsRejected()
    {
        var client = await factory.CreateAdminClientAsync();
        var response = await client.PostAsJsonAsync("/api/workflows", new JsonObject
        {
            ["name"] = $"Cron {Guid.NewGuid():N}", ["nodes"] = new JsonArray(Node("tick", "schedule", new JsonObject { ["Repeat"] = "Cron", ["Cron"] = "every day" })), ["edges"] = new JsonArray()
        }, _ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync(_ct)).Should().Contain("Cron");
    }

    [Fact]
    public async Task WebhookTriggerRunsWorkflowWithTheRequestBodyAsItems()
    {
        var client = await factory.CreateAdminClientAsync();
        var id = await CreateAsync(client, true, [Node("hook", "webhook")]);
        var created = await client.PostAsync($"/api/workflows/{id}/webhooks/hook/token", null, _ct);
        created.StatusCode.Should().Be(HttpStatusCode.OK, await created.Content.ReadAsStringAsync(_ct));
        var url = (await created.Content.ReadFromJsonAsync<JsonObject>(_ct))!["url"]!.GetValue<string>();
        var anonymous = factory.CreateClient();

        var response = await anonymous.PostAsJsonAsync(url, new JsonArray(new JsonObject { ["id"] = 1 }, new JsonObject { ["id"] = 2 }), _ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(_ct));
        var runId = (await response.Content.ReadFromJsonAsync<JsonObject>(_ct))!["executionId"]!.GetValue<string>();
        var run = (await client.GetFromJsonAsync<JsonObject>($"/api/workflow-executions/{runId}", _ct))!;
        run["triggerNodeId"]!.GetValue<string>().Should().Be("hook");
        run["nodes"]![0]!["rowCount"]!.GetValue<int>().Should().Be(2);
        (await anonymous.PostAsync("/webhooks/workflows/reef_wh_" + new string('a', 43), null, _ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SavedWorkflowKeepsTheStoredWebhookTokenHash()
    {
        var client = await factory.CreateAdminClientAsync();
        var id = await CreateAsync(client, true, [Node("hook", "webhook")]);
        var url = (await (await client.PostAsync($"/api/workflows/{id}/webhooks/hook/token", null, _ct)).Content.ReadFromJsonAsync<JsonObject>(_ct))!["url"]!.GetValue<string>();
        var workflow = (await client.GetFromJsonAsync<JsonObject>($"/api/workflows/{id}", _ct))!;
        workflow["nodes"]![0]!["config"]!["TokenHash"] = "forged";

        (await client.PutAsJsonAsync($"/api/workflows/{id}", workflow, _ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await factory.CreateClient().PostAsync(url, null, _ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task InactiveWorkflowRejectsWebhookCalls()
    {
        var client = await factory.CreateAdminClientAsync();
        var id = await CreateAsync(client, false, [Node("hook", "webhook")]);
        var url = (await (await client.PostAsync($"/api/workflows/{id}/webhooks/hook/token", null, _ct)).Content.ReadFromJsonAsync<JsonObject>(_ct))!["url"]!.GetValue<string>();

        (await factory.CreateClient().PostAsync(url, null, _ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task NodeSecretsAreEncryptedMaskedAndUsedByTheRun()
    {
        var client = await factory.CreateAdminClientAsync();
        using var api = new MockHttpServer();
        api.SetResponse(200, "[]");
        var id = await CreateAsync(client, false,
            [
                Node("start", "manual"),
                Node("page", "fetch", new JsonObject
                {
                    ["From"] = "Http", ["Settings"] = new JsonObject { ["url"] = api.BaseUrl + "items", ["authType"] = "Bearer", ["authToken"] = "s3cret-token" }
                })
            ],
            [new JsonObject { ["from"] = "start", ["to"] = "page" }]);

        var workflow = (await client.GetFromJsonAsync<JsonObject>($"/api/workflows/{id}", _ct))!;
        workflow["nodes"]![1]!["config"]!["Settings"]!["authToken"]!.GetValue<string>().Should().Be("[SECRET]");
        await using (var db = new SqliteConnection($"Data Source={Path.Combine(factory.DataFolder, "Reef.db")}"))
        {
            await db.OpenAsync(_ct);
            await using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT group_concat(DocumentJson) FROM Workflows";
            ((string)(await cmd.ExecuteScalarAsync(_ct))!).Should().NotContain("s3cret-token");
        }
        (await client.PutAsJsonAsync($"/api/workflows/{id}", workflow, _ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await client.PostAsync($"/api/workflows/{id}/run", null, _ct)).StatusCode.Should().Be(HttpStatusCode.OK);

        api.LastAuthorizationHeader.Should().Be("Bearer s3cret-token");
    }
}
