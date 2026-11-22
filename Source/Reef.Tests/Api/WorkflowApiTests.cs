using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Xunit;

namespace Reef.Tests.Api;

[Collection(ApiCollection.Name)]
public class WorkflowApiTests(ReefApiFactory factory)
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private static JsonObject Node(string id, string type, JsonObject? config = null, double x = 0) =>
        new() { ["id"] = id, ["type"] = type, ["x"] = x, ["y"] = 40, ["config"] = config ?? new JsonObject() };

    private static JsonObject Edge(string from, string to, string fromPort = "main", string toPort = "main") =>
        new() { ["from"] = from, ["fromPort"] = fromPort, ["to"] = to, ["toPort"] = toPort };

    private async Task<(HttpClient Client, string ConnectionId, string DestinationId)> SetupAsync()
    {
        var client = await factory.CreateAdminClientAsync();
        var created = await client.PostAsJsonAsync("/api/connections", new { name = $"Shop {Guid.NewGuid():N}", type = "Sqlite",
            connectionString = $"Data Source={Path.Combine(factory.DataFolder, "shop.db")}", isActive = true, hash = "" }, _ct);
        created.EnsureSuccessStatusCode();
        var connectionId = (await created.Content.ReadFromJsonAsync<JsonObject>(_ct))!["id"]!.GetValue<string>();
        var destinations = await client.GetFromJsonAsync<JsonArray>("/api/destinations", _ct);
        var destinationId = destinations!.First(d => d!["type"]!.GetValue<string>() == "Local")!["id"]!.GetValue<string>();
        return (client, connectionId, destinationId);
    }

    private static JsonObject Branching(string connectionId, string destinationId) => new()
    {
        ["name"] = $"Orders {Guid.NewGuid():N}",
        ["nodes"] = new JsonArray(
            Node("start", "manual"),
            Node("orders", "query", new JsonObject { ["ConnectionId"] = connectionId, ["Query"] = "SELECT * FROM Orders" }, 200),
            Node("big", "if", new JsonObject { ["Column"] = "Total", ["Operator"] = "greaterThan", ["Value"] = "100" }, 400),
            Node("file", "destination", new JsonObject { ["DestinationId"] = destinationId, ["Format"] = "CSV" }, 600)),
        ["edges"] = new JsonArray(Edge("start", "orders"), Edge("orders", "big"), Edge("big", "file", fromPort: "true"))
    };

    [Fact]
    public async Task ParallelSavesAllSucceed()
    {
        var (client, connectionId, destinationId) = await SetupAsync();

        var saves = await Task.WhenAll(Enumerable.Range(0, 24).Select(_ => client.PostAsJsonAsync("/api/workflows", Branching(connectionId, destinationId), _ct)));

        saves.Select(r => r.StatusCode).Should().AllBeEquivalentTo(HttpStatusCode.Created);
    }

    [Fact]
    public async Task WorkflowSavesAndReadsBackWithEdgesAndPublicIds()
    {
        var (client, connectionId, destinationId) = await SetupAsync();

        var created = await client.PostAsJsonAsync("/api/workflows", Branching(connectionId, destinationId), _ct);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(_ct));
        var id = (await created.Content.ReadFromJsonAsync<JsonObject>(_ct))!["id"]!.GetValue<string>();
        var read = await client.GetFromJsonAsync<JsonObject>($"/api/workflows/{id}", _ct);

        id.Should().MatchRegex("^[0-9A-Z]{26}$");
        read!["version"]!.GetValue<int>().Should().Be(1);
        read["edges"]!.AsArray().Should().HaveCount(3);
        read["edges"]![2]!["fromPort"]!.GetValue<string>().Should().Be("true");
        var query = read["nodes"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == "orders")!;
        query["x"]!.GetValue<double>().Should().Be(200);
        query["config"]!["ConnectionId"]!.GetValue<string>().Should().Be(connectionId);

        read["name"] = read["name"]!.GetValue<string>() + " v2";
        var updated = await client.PutAsJsonAsync($"/api/workflows/{id}", read, _ct);
        updated.StatusCode.Should().Be(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync(_ct));
        (await updated.Content.ReadFromJsonAsync<JsonObject>(_ct))!["version"]!.GetValue<int>().Should().Be(2);

        var list = await client.GetFromJsonAsync<JsonArray>("/api/workflows", _ct);
        list!.Should().Contain(w => w!["id"]!.GetValue<string>() == id);

        (await client.DeleteAsync($"/api/workflows/{id}", _ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync($"/api/workflows/{id}", _ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task InvalidWorkflowReturnsErrorsPerNodeAndField()
    {
        var (client, _, _) = await SetupAsync();
        var workflow = new JsonObject
        {
            ["name"] = $"Broken {Guid.NewGuid():N}",
            ["maxPasses"] = 0,
            ["nodes"] = new JsonArray(Node("a", "query"), Node("b", "if", new JsonObject { ["Column"] = "Total", ["Operator"] = "isEmpty" }), Node("c", "nope")),
            ["edges"] = new JsonArray(Edge("a", "b"), Edge("b", "a", fromPort: "true"), Edge("b", "a", fromPort: "maybe"), Edge("a", "ghost"))
        };

        var response = await client.PostAsJsonAsync("/api/workflows", workflow, _ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var errors = (await response.Content.ReadFromJsonAsync<JsonObject>(_ct))!["errors"]!.AsArray()
            .Select(e => (Node: e!["nodeId"]?.GetValue<string>(), Field: e["field"]?.GetValue<string>(), Message: e["message"]!.GetValue<string>())).ToList();
        errors.Should().Contain(e => e.Node == "a" && e.Field == "Query");
        errors.Should().Contain(e => e.Node == "c" && e.Message.Contains("Unknown node type"));
        errors.Should().Contain(e => e.Node == "b" && e.Message.Contains("maybe"));
        errors.Should().Contain(e => e.Message.Contains("ghost"));
        errors.Should().NotContain(e => e.Message.Contains("loop"));
        errors.Should().Contain(e => e.Field == "MaxPasses");
        errors.Should().Contain(e => e.Message.Contains("trigger"));
    }

    [Fact]
    public async Task DuplicateWorkflowNameReturnsConflict()
    {
        var (client, connectionId, destinationId) = await SetupAsync();
        var workflow = Branching(connectionId, destinationId);
        (await client.PostAsJsonAsync("/api/workflows", workflow, _ct)).EnsureSuccessStatusCode();

        workflow["name"] = workflow["name"]!.GetValue<string>().ToUpperInvariant();
        var duplicate = await client.PostAsJsonAsync("/api/workflows", workflow, _ct);

        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await duplicate.Content.ReadFromJsonAsync<JsonObject>(_ct))!["field"]!.GetValue<string>().Should().Be("Name");
    }

    [Fact]
    public async Task CatalogListsGroupedNodesWithPorts()
    {
        var client = await factory.CreateAdminClientAsync();

        var catalog = await client.GetFromJsonAsync<JsonObject>("/api/workflows/catalog", _ct);

        var nodes = catalog!["nodes"]!.AsArray();
        var condition = nodes.Single(n => n!["type"]!.GetValue<string>() == "if")!;
        condition["group"]!.GetValue<string>().Should().Be("Flow");
        condition["outputs"]!.AsArray().Select(p => p!.GetValue<string>()).Should().Equal("true", "false");
        nodes.Single(n => n!["type"]!.GetValue<string>() == "manual")!["inputs"]!.AsArray().Should().BeEmpty();
        nodes.Select(n => n!["group"]!.GetValue<string>()).Distinct().Should().BeEquivalentTo("Triggers", "Sources", "Transform", "Flow", "Deliver", "Hooks");
    }
}
