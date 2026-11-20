using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Reef.Tests.Api;

[Collection(ApiCollection.Name)]
public class WorkflowRunApiTests(ReefApiFactory factory)
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;
    private string _shopDb = "";

    private static JsonObject Node(string id, string type, JsonObject? config = null) =>
        new() { ["id"] = id, ["type"] = type, ["x"] = 0, ["y"] = 0, ["config"] = config ?? new JsonObject() };

    private static JsonObject Edge(string from, string to, string fromPort = "main", string toPort = "main") =>
        new() { ["from"] = from, ["fromPort"] = fromPort, ["to"] = to, ["toPort"] = toPort };

    private async Task<(HttpClient Client, string ConnectionId, string DestinationId)> SetupAsync()
    {
        var shopDb = Path.Combine(factory.DataFolder, $"shop-{Guid.NewGuid():N}.db");
        await using (var db = new SqliteConnection($"Data Source={shopDb}"))
        {
            await db.OpenAsync(_ct);
            await using var cmd = db.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE Orders (OrderId INTEGER PRIMARY KEY, Customer TEXT, Total REAL, Region TEXT);
                INSERT INTO Orders VALUES (1, 'Alpha', 50, 'North'), (2, 'Beta', 150, 'South'), (3, 'Gamma', 250, 'North'), (4, 'Delta', 75, 'West');
                CREATE TABLE Regions (Region TEXT, Manager TEXT);
                CREATE TABLE RunLog (Entry TEXT);
                INSERT INTO Regions VALUES ('North', 'Noor'), ('South', 'Sam');
                """;
            await cmd.ExecuteNonQueryAsync(_ct);
        }
        var client = await factory.CreateAdminClientAsync();
        var created = await client.PostAsJsonAsync("/api/connections", new { name = $"Shop {Guid.NewGuid():N}", type = "Sqlite", connectionString = $"Data Source={shopDb}", isActive = true, hash = "" }, _ct);
        created.EnsureSuccessStatusCode();
        var connectionId = (await created.Content.ReadFromJsonAsync<JsonObject>(_ct))!["id"]!.GetValue<string>();
        var destinations = await client.GetFromJsonAsync<JsonArray>("/api/destinations", _ct);
        var destinationId = destinations!.First(d => d!["type"]!.GetValue<string>() == "Local")!["id"]!.GetValue<string>();
        _shopDb = shopDb;
        return (client, connectionId, destinationId);
    }

    private async Task<JsonObject> RunAsync(HttpClient client, JsonArray nodes, JsonArray edges)
    {
        var created = await client.PostAsJsonAsync("/api/workflows", new JsonObject { ["name"] = $"Run {Guid.NewGuid():N}", ["nodes"] = nodes, ["edges"] = edges }, _ct);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(_ct));
        var id = (await created.Content.ReadFromJsonAsync<JsonObject>(_ct))!["id"]!.GetValue<string>();
        var run = await client.PostAsync($"/api/workflows/{id}/run", null, _ct);
        run.StatusCode.Should().Be(HttpStatusCode.OK, await run.Content.ReadAsStringAsync(_ct));
        var runId = (await run.Content.ReadFromJsonAsync<JsonObject>(_ct))!["id"]!.GetValue<string>();
        return (await client.GetFromJsonAsync<JsonObject>($"/api/workflow-executions/{runId}", _ct))!;
    }

    private static JsonObject NodeRun(JsonObject run, string nodeId) =>
        run["nodes"]!.AsArray().Single(n => n!["nodeId"]!.GetValue<string>() == nodeId)!.AsObject();

    private static JsonObject Output(JsonObject run, string nodeId, string port = "main") =>
        JsonNode.Parse(NodeRun(run, nodeId)["outputSampleJson"]!.GetValue<string>())![port]!.AsObject();

    [Fact]
    public async Task IfBranchWritesMatchingRowsAndSkipsEmptyBranch()
    {
        var (client, connectionId, destinationId) = await SetupAsync();

        var run = await RunAsync(client,
            [
                Node("start", "manual"),
                Node("orders", "query", new JsonObject { ["ConnectionId"] = connectionId, ["Query"] = "SELECT OrderId, Customer, Total FROM Orders ORDER BY OrderId" }),
                Node("big", "if", new JsonObject { ["Column"] = "Total", ["Operator"] = "greaterThan", ["Value"] = "1000" }),
                Node("small", "if", new JsonObject { ["Column"] = "Total", ["Operator"] = "lessThan", ["Value"] = "100" }),
                Node("bigfile", "destination", new JsonObject { ["OutputDestinationId"] = destinationId, ["OutputFormat"] = "CSV" }),
                Node("smallfile", "destination", new JsonObject { ["OutputDestinationId"] = destinationId, ["OutputFormat"] = "CSV" })
            ],
            [Edge("start", "orders"), Edge("orders", "big"), Edge("orders", "small"), Edge("big", "bigfile", "true"), Edge("small", "smallfile", "true")]);

        run["status"]!.GetValue<string>().Should().Be("Success", run.ToJsonString());
        NodeRun(run, "bigfile")["status"]!.GetValue<string>().Should().Be("Skipped");
        NodeRun(run, "smallfile")["rowCount"]!.GetValue<int>().Should().Be(2);
        var orders = Output(run, "orders");
        orders["columns"]!.AsArray().Select(c => c!["name"]!.GetValue<string>()).Should().Equal("OrderId", "Customer", "Total");
        var file = Output(run, "smallfile")["files"]![0]!["path"]!.GetValue<string>();
        var lines = await File.ReadAllLinesAsync(file, _ct);
        lines.Skip(1).Where(l => l.Length > 0).Should().HaveCount(2).And.Contain(l => l.Contains("Alpha")).And.Contain(l => l.Contains("Delta"));
    }

    [Fact]
    public async Task SwitchRoutesRowsAndMergeJoinsBranches()
    {
        var (client, connectionId, _) = await SetupAsync();

        var run = await RunAsync(client,
            [
                Node("start", "manual"),
                Node("orders", "query", new JsonObject { ["ConnectionId"] = connectionId, ["Query"] = "SELECT OrderId, Region FROM Orders" }),
                Node("regions", "query", new JsonObject { ["ConnectionId"] = connectionId, ["Query"] = "SELECT Region, Manager FROM Regions" }),
                Node("route", "switch", new JsonObject { ["Column"] = "Region", ["Cases"] = new JsonArray(new JsonObject { ["Value"] = "north" }) }),
                Node("join", "merge", new JsonObject { ["Mode"] = "join", ["JoinColumn"] = "Region" })
            ],
            [Edge("start", "orders"), Edge("start", "regions"), Edge("orders", "route"), Edge("route", "join", "north", "input1"), Edge("regions", "join", toPort: "input2")]);

        run["status"]!.GetValue<string>().Should().Be("Success", run.ToJsonString());
        Output(run, "route", "north")["total"]!.GetValue<int>().Should().Be(2);
        Output(run, "route", "fallback")["total"]!.GetValue<int>().Should().Be(2);
        var joined = Output(run, "join")["items"]!.AsArray();
        joined.Should().HaveCount(2).And.OnlyContain(r => r!["Manager"]!.GetValue<string>() == "Noor");
    }

    [Fact]
    public async Task FailedNodeFailsRunAndStopsDownstreamNodes()
    {
        var (client, connectionId, destinationId) = await SetupAsync();

        var run = await RunAsync(client,
            [
                Node("start", "manual"),
                Node("orders", "query", new JsonObject { ["ConnectionId"] = connectionId, ["Query"] = "SELECT * FROM Missing" }),
                Node("file", "destination", new JsonObject { ["OutputDestinationId"] = destinationId, ["OutputFormat"] = "JSON" })
            ],
            [Edge("start", "orders"), Edge("orders", "file")]);

        run["status"]!.GetValue<string>().Should().Be("Failed");
        run["errorMessage"]!.GetValue<string>().Should().Contain("orders").And.Contain("Missing");
        NodeRun(run, "orders")["status"]!.GetValue<string>().Should().Be("Failed");
        run["nodes"]!.AsArray().Should().NotContain(n => n!["nodeId"]!.GetValue<string>() == "file");
    }
    [Fact]
    public async Task TemplateNodeRendersRowsAndDestinationUploadsTheFile()
    {
        var (client, connectionId, destinationId) = await SetupAsync();
        var template = await client.PostAsJsonAsync("/api/templates", new { name = $"Lines {Guid.NewGuid():N}", type = "ScribanTemplate", outputFormat = "CSV",
            template = "{{ for row in data }}{{ row.Customer }};{{ end }}" }, _ct);
        template.EnsureSuccessStatusCode();
        var templateId = (await template.Content.ReadFromJsonAsync<JsonObject>(_ct))!["id"]!.GetValue<string>();

        var run = await RunAsync(client,
            [
                Node("start", "manual"),
                Node("orders", "query", new JsonObject { ["ConnectionId"] = connectionId, ["Query"] = "SELECT Customer FROM Orders ORDER BY OrderId" }),
                Node("render", "template", new JsonObject { ["TemplateId"] = templateId }),
                Node("file", "destination", new JsonObject { ["OutputDestinationId"] = destinationId, ["OutputFormat"] = "Files" })
            ],
            [Edge("start", "orders"), Edge("orders", "render"), Edge("render", "file")]);

        run["status"]!.GetValue<string>().Should().Be("Success", run.ToJsonString());
        var files = Output(run, "file")["files"]!.AsArray();
        files.Should().HaveCount(1);
        (await File.ReadAllTextAsync(files[0]!["path"]!.GetValue<string>(), _ct)).Should().Be("Alpha;Beta;Gamma;Delta;");
    }

    [Fact]
    public async Task ProcessNodesRunStatementsAroundTheQuery()
    {
        var (client, connectionId, _) = await SetupAsync();
        JsonObject Hook(string prefix, string command, bool continueOnError = false) => new()
        {
            ["ConnectionId"] = connectionId, [$"{prefix}Type"] = "Query",
            [$"{prefix}Config"] = new JsonObject { ["Command"] = command, ["Timeout"] = 30, ["ContinueOnError"] = continueOnError }
        };

        var run = await RunAsync(client,
            [
                Node("start", "manual"),
                Node("pre", "preprocess", Hook("PreProcess", "INSERT INTO RunLog VALUES ('pre')")),
                Node("log", "query", new JsonObject { ["ConnectionId"] = connectionId, ["Query"] = "SELECT Entry FROM RunLog" }),
                Node("post", "postprocess", Hook("PostProcess", "INSERT INTO RunLog VALUES ('post {rowcount}')")),
                Node("broken", "postprocess", Hook("PostProcess", "INSERT INTO Nowhere VALUES (1)", continueOnError: true))
            ],
            [Edge("start", "pre"), Edge("pre", "log"), Edge("log", "post"), Edge("post", "broken")]);

        run["status"]!.GetValue<string>().Should().Be("Success", run.ToJsonString());
        Output(run, "log")["items"]![0]!["Entry"]!.GetValue<string>().Should().Be("pre");
        NodeRun(run, "broken")["errorMessage"]!.GetValue<string>().Should().Contain("Nowhere");
        await using var db = new SqliteConnection($"Data Source={_shopDb}");
        await db.OpenAsync(_ct);
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT Entry FROM RunLog WHERE Entry LIKE 'post%'";
        (await cmd.ExecuteScalarAsync(_ct)).Should().Be("post 1");
    }
}
