using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Reef.Tests.Api;

// Workflow helpers over the api
public static class TestWorkflows
{
    public static JsonObject Node(string id, string type, JsonObject? config = null) =>
        new() { ["id"] = id, ["type"] = type, ["x"] = 0, ["y"] = 0, ["config"] = config ?? new JsonObject() };

    public static JsonObject Edge(string from, string to, string fromPort = "main", string toPort = "main") =>
        new() { ["from"] = from, ["fromPort"] = fromPort, ["to"] = to, ["toPort"] = toPort };

    // Chains steps after a manual trigger
    public static async Task<string> CreateChainAsync(HttpClient client, params JsonObject[] steps)
    {
        var nodes = new JsonArray(Node("start", "manual"));
        var edges = new JsonArray();
        var previous = "start";
        foreach (var step in steps)
        {
            nodes.Add(step);
            var id = step["id"]!.GetValue<string>();
            edges.Add(Edge(previous, id));
            previous = id;
        }
        var created = await client.PostAsJsonAsync("/api/workflows", new JsonObject { ["name"] = $"Test {Guid.NewGuid():N}", ["nodes"] = nodes, ["edges"] = edges }, TestContext.Current.CancellationToken);
        created.EnsureSuccessStatusCode();
        return (await created.Content.ReadFromJsonAsync<JsonObject>(TestContext.Current.CancellationToken))!["id"]!.GetValue<string>();
    }

    public static async Task<JsonObject> RunAsync(HttpClient client, string workflowId)
    {
        var ct = TestContext.Current.CancellationToken;
        var run = await client.PostAsync($"/api/workflows/{workflowId}/run", null, ct);
        run.EnsureSuccessStatusCode();
        var runId = (await run.Content.ReadFromJsonAsync<JsonObject>(ct))!["id"]!.GetValue<string>();
        return (await client.GetFromJsonAsync<JsonObject>($"/api/workflow-executions/{runId}", ct))!;
    }

    public static JsonObject? NodeRun(JsonObject run, string nodeId) =>
        run["nodes"]!.AsArray().LastOrDefault(n => n!["nodeId"]!.GetValue<string>() == nodeId)?.AsObject();
}
