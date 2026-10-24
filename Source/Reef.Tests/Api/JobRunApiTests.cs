using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Reef.Tests.Api;

[Collection(ApiCollection.Name)]
public class JobRunApiTests(ReefApiFactory factory)
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private const string PdfTemplate = """
        {{! format: pdf }}
        {{! pageSize: A4 }}

        {{# content }}
        <h1>Order {{ rows[0].id }}</h1>
        {{/ content }}
        """;

    [Fact]
    public async Task TriggeredPdfJobCompletes()
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
        var templateId = await IdOfAsync(await client.PostAsJsonAsync("/api/templates", new { name = $"Pdf {Guid.NewGuid():N}", type = "DocumentTemplate", template = PdfTemplate, outputFormat = "PDF" }, _ct));
        var destinations = await client.GetFromJsonAsync<JsonArray>("/api/destinations", _ct);
        var destinationId = destinations!.First(d => d!["type"]!.GetValue<string>() == "Local")!["id"]!.GetValue<string>();
        var document = new JsonObject
        {
            ["kind"] = "export",
            ["settings"] = new JsonObject { ["Name"] = $"Pdf job {Guid.NewGuid():N}", ["IsEnabled"] = true },
            ["nodes"] = new JsonArray(
                new JsonObject { ["id"] = "source", ["type"] = "source", ["x"] = 0, ["y"] = 0, ["config"] = new JsonObject { ["ConnectionId"] = connectionId, ["Query"] = "SELECT id FROM Orders" } },
                new JsonObject { ["id"] = "template", ["type"] = "template", ["x"] = 150, ["y"] = 0, ["config"] = new JsonObject { ["TemplateId"] = templateId } },
                new JsonObject { ["id"] = "destination", ["type"] = "destination", ["x"] = 300, ["y"] = 0, ["config"] = new JsonObject { ["OutputDestinationId"] = destinationId, ["OutputFormat"] = "JSON" } })
        };
        var profileId = await IdOfAsync(await client.PostAsJsonAsync("/api/profile-documents/export", document, _ct));
        var jobId = await IdOfAsync(await client.PostAsJsonAsync("/api/jobs", new { name = $"Pdf job {Guid.NewGuid():N}", type = "ProfileExecution", profileId, scheduleType = "Manual", isEnabled = true }, _ct));

        (await client.PostAsJsonAsync($"/api/jobs/{jobId}/trigger", new { }, _ct)).EnsureSuccessStatusCode();

        var status = "Running";
        for (var i = 0; i < 60 && status == "Running"; i++)
        {
            await Task.Delay(500, _ct);
            var latest = await client.GetFromJsonAsync<JsonObject>($"/api/jobs/{jobId}/executions/latest", _ct);
            status = latest!["status"]!.GetValue<string>();
        }
        status.Should().Be("Completed", (await client.GetStringAsync($"/api/jobs/{jobId}/executions/latest", _ct)));
    }

    private async Task<string> IdOfAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync(_ct);
        response.IsSuccessStatusCode.Should().BeTrue(body);
        return JsonNode.Parse(body)!["id"]!.GetValue<string>();
    }
}
