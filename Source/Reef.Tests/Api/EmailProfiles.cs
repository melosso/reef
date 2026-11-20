using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Reef.Tests.Api;

// Builds email export profiles through the api, the query reads from a fresh sqlite file.
public static class EmailProfiles
{
    public static string Rows(int count) =>
        $"WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n WHERE i < {count}) SELECT i AS Id, 'user' || i || '@example.com' AS Email FROM n";

    public static object Smtp(int port) => new { EmailProvider = "SMTP", SmtpServer = "127.0.0.1", SmtpPort = port, SecurityMode = "None", FromAddress = "reef@example.com", TimeoutSeconds = 5 };

    public static async Task<string> IdOfAsync(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<JsonObject>())!["id"]!.GetValue<string>();
    }

    public static async Task<string> CreateAsync(HttpClient admin, string dataFolder, object smtp, string query, JsonObject? mail = null, string template = "<p>{{ rows[0].Email }}</p>")
    {
        var ct = TestContext.Current.CancellationToken;
        var shopDb = Path.Combine(dataFolder, $"mail-{Guid.NewGuid():N}.db");
        var connectionId = await IdOfAsync(await admin.PostAsJsonAsync("/api/connections", new { name = $"Mail {Guid.NewGuid():N}", type = "Sqlite", connectionString = $"Data Source={shopDb}", isActive = true, hash = "" }, ct));
        var templateId = await IdOfAsync(await admin.PostAsJsonAsync("/api/templates", new { name = $"Mail {Guid.NewGuid():N}", type = "ScribanTemplate", outputFormat = "HTML", template }, ct));
        var destinationId = await IdOfAsync(await admin.PostAsJsonAsync("/api/destinations", new { name = $"Mail {Guid.NewGuid():N}", type = "Email", configurationJson = JsonSerializer.Serialize(smtp) }, ct));
        var config = new JsonObject
        {
            ["OutputDestinationId"] = destinationId, ["EmailTemplateId"] = templateId, ["EmailRecipientsColumn"] = "Email",
            ["UseHardcodedSubject"] = true, ["EmailSubjectHardcoded"] = "Hi"
        };
        foreach (var (key, value) in mail ?? []) config[key] = value?.DeepClone();
        var document = new JsonObject
        {
            ["kind"] = "export",
            ["settings"] = new JsonObject { ["Name"] = $"Mail {Guid.NewGuid():N}", ["IsEnabled"] = true },
            ["nodes"] = new JsonArray(
                new JsonObject { ["id"] = "source", ["type"] = "source", ["x"] = 0, ["y"] = 0, ["config"] = new JsonObject { ["ConnectionId"] = connectionId, ["Query"] = query } },
                new JsonObject { ["id"] = "mail", ["type"] = "emailexport", ["x"] = 300, ["y"] = 0, ["config"] = config })
        };
        return await IdOfAsync(await admin.PostAsJsonAsync("/api/profile-documents/export", document, ct));
    }

    public static async Task<JsonNode> RunAsync(HttpClient admin, string profileId)
    {
        var ct = TestContext.Current.CancellationToken;
        (await admin.PostAsJsonAsync($"/api/executions/execute/{profileId}", new { }, ct)).EnsureSuccessStatusCode();
        var executions = await admin.GetFromJsonAsync<JsonArray>($"/api/executions/by-profile/{profileId}", ct);
        return executions!.First()!;
    }
}
