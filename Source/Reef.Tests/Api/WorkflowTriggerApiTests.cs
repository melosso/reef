using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Reef.Core.Workflows.Triggers;
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
}
