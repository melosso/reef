using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Reef.Tests.Api;

[Collection(ApiCollection.Name)]
public class WebhookApiTests(ReefApiFactory factory)
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Fact]
    public async Task ProfileWebhookAcceptsAndRunsInBackground()
    {
        var client = await factory.CreateAdminClientAsync();
        var shopDb = Path.Combine(factory.DataFolder, $"shop-{Guid.NewGuid():N}.db");
        await using (var db = new SqliteConnection($"Data Source={shopDb}"))
        {
            await db.OpenAsync(_ct);
            await using var cmd = db.CreateCommand();
            cmd.CommandText = "CREATE TABLE Orders (id INTEGER); INSERT INTO Orders VALUES (7);";
            await cmd.ExecuteNonQueryAsync(_ct);
        }

        var connectionId = await IdOfAsync(await client.PostAsJsonAsync("/api/connections", new { name = $"Shop {Guid.NewGuid():N}", type = "Sqlite", connectionString = $"Data Source={shopDb}", isActive = true, hash = "" }, _ct));
        var destinations = await client.GetFromJsonAsync<JsonArray>("/api/destinations", _ct);
        var destinationId = destinations!.First(d => d!["type"]!.GetValue<string>() == "Local")!["id"]!.GetValue<string>();
        var document = new JsonObject
        {
            ["kind"] = "export",
            ["settings"] = new JsonObject { ["Name"] = $"Webhook {Guid.NewGuid():N}", ["IsEnabled"] = true },
            ["nodes"] = new JsonArray(
                new JsonObject { ["id"] = "source", ["type"] = "source", ["x"] = 0, ["y"] = 0, ["config"] = new JsonObject { ["ConnectionId"] = connectionId, ["Query"] = "SELECT id FROM Orders" } },
                new JsonObject { ["id"] = "destination", ["type"] = "destination", ["x"] = 300, ["y"] = 0, ["config"] = new JsonObject { ["OutputDestinationId"] = destinationId, ["OutputFormat"] = "JSON" } })
        };
        var profileId = await IdOfAsync(await client.PostAsJsonAsync("/api/profile-documents/export", document, _ct));
        var webhook = await client.PostAsJsonAsync("/api/webhooks", new { profileId }, _ct);
        var token = (await webhook.Content.ReadFromJsonAsync<JsonObject>(_ct))!["token"]!.GetValue<string>();

        var trigger = await factory.CreateClient().PostAsJsonAsync($"/webhooks/{token}", new { }, _ct);

        trigger.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var executionId = (await trigger.Content.ReadFromJsonAsync<JsonObject>(_ct))!["executionId"]!.GetValue<string>();
        var status = "Running";
        for (var i = 0; i < 60 && status == "Running"; i++)
        {
            await Task.Delay(500, _ct);
            status = (await client.GetFromJsonAsync<JsonObject>($"/api/executions/{executionId}", _ct))!["status"]!.GetValue<string>();
        }
        status.Should().Be("Success");
    }

    private async Task<string> IdOfAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(_ct);
        response.IsSuccessStatusCode.Should().BeTrue(body);
        return JsonNode.Parse(body)!["id"]!.GetValue<string>();
    }
}
