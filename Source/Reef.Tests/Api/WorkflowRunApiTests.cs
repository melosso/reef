using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Reef.Tests.Destinations;
using Reef.Tests.Templates;
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
                CREATE TABLE ImportedOrders (OrderId INTEGER PRIMARY KEY, CustomerName TEXT);
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

    private async Task<JsonObject> RunAsync(HttpClient client, JsonArray nodes, JsonArray edges) =>
        await RunAgainAsync(client, await CreateAsync(client, nodes, edges));

    private async Task<string> CreateAsync(HttpClient client, JsonArray nodes, JsonArray edges, int maxPasses = 1000)
    {
        var created = await client.PostAsJsonAsync("/api/workflows", new JsonObject { ["name"] = $"Run {Guid.NewGuid():N}", ["nodes"] = nodes, ["edges"] = edges, ["maxPasses"] = maxPasses }, _ct);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(_ct));
        return (await created.Content.ReadFromJsonAsync<JsonObject>(_ct))!["id"]!.GetValue<string>();
    }

    private async Task<JsonObject> RunAgainAsync(HttpClient client, string id)
    {
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
                Node("bigfile", "destination", new JsonObject { ["DestinationId"] = destinationId, ["Format"] = "CSV" }),
                Node("smallfile", "destination", new JsonObject { ["DestinationId"] = destinationId, ["Format"] = "CSV" })
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
                Node("file", "destination", new JsonObject { ["DestinationId"] = destinationId, ["Format"] = "JSON" })
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
                Node("file", "destination", new JsonObject { ["DestinationId"] = destinationId, ["Format"] = "Files" })
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
        JsonObject Hook(string command, bool continueOnError = false) => new()
        {
            ["ConnectionId"] = connectionId, ["Runs"] = "Query", ["Command"] = command, ["ContinueOnError"] = continueOnError
        };

        var run = await RunAsync(client,
            [
                Node("start", "manual"),
                Node("pre", "command", Hook("INSERT INTO RunLog VALUES ('pre')")),
                Node("log", "query", new JsonObject { ["ConnectionId"] = connectionId, ["Query"] = "SELECT Entry FROM RunLog" }),
                Node("post", "command", Hook("INSERT INTO RunLog VALUES ('post {rowcount}')")),
                Node("broken", "command", Hook("INSERT INTO Nowhere VALUES (1)", continueOnError: true))
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
    [Fact]
    public async Task SplitNodeWritesOneFilePerColumnValue()
    {
        var (client, connectionId, destinationId) = await SetupAsync();

        var run = await RunAsync(client,
            [
                Node("start", "manual"),
                Node("orders", "query", new JsonObject { ["ConnectionId"] = connectionId, ["Query"] = "SELECT OrderId, Region FROM Orders" }),
                Node("split", "split", new JsonObject { ["Column"] = "Region", ["FileName"] = "orders_{splitkey}.{format}", ["ExcludeColumn"] = true }),
                Node("file", "destination", new JsonObject { ["DestinationId"] = destinationId, ["Format"] = "CSV" })
            ],
            [Edge("start", "orders"), Edge("orders", "split"), Edge("split", "file")]);

        run["status"]!.GetValue<string>().Should().Be("Success", run.ToJsonString());
        var files = Output(run, "file")["files"]!.AsArray().Select(f => f!["path"]!.GetValue<string>()).ToList();
        files.Select(Path.GetFileName).Should().BeEquivalentTo("orders_North.csv", "orders_South.csv", "orders_West.csv");
        var north = await File.ReadAllLinesAsync(files.Single(f => f.EndsWith("orders_North.csv")), _ct);
        north[0].Should().NotContain("Region");
        north.Skip(1).Count(l => l.Length > 0).Should().Be(2);
    }
    [Fact]
    public async Task SmartSyncPassesChangedRowsAndSavesStateOnlyAfterSuccess()
    {
        var (client, connectionId, _) = await SetupAsync();
        JsonArray Nodes(string after) =>
        [
            Node("start", "manual"),
            Node("orders", "query", new JsonObject { ["ConnectionId"] = connectionId, ["Query"] = "SELECT OrderId, Total FROM Orders" }),
            Node("sync", "deltasync", new JsonObject { ["IdColumn"] = "OrderId" }),
            Node("after", "command", new JsonObject { ["ConnectionId"] = connectionId, ["Runs"] = "Query", ["Command"] = after })
        ];
        JsonArray Edges() => [Edge("start", "orders"), Edge("orders", "sync"), Edge("sync", "after")];

        var failing = await CreateAsync(client, Nodes("INSERT INTO Nowhere VALUES (1)"), Edges());
        (await RunAgainAsync(client, failing))["status"]!.GetValue<string>().Should().Be("Failed");
        NodeRun(await RunAgainAsync(client, failing), "sync")["rowCount"]!.GetValue<int>().Should().Be(4);

        var working = await CreateAsync(client, Nodes("SELECT 1"), Edges());
        NodeRun(await RunAgainAsync(client, working), "sync")["rowCount"]!.GetValue<int>().Should().Be(4);
        await using (var db = new SqliteConnection($"Data Source={_shopDb}"))
        {
            await db.OpenAsync(_ct);
            await using var cmd = db.CreateCommand();
            cmd.CommandText = "UPDATE Orders SET Total = 999 WHERE OrderId = 2";
            await cmd.ExecuteNonQueryAsync(_ct);
        }
        var second = await RunAgainAsync(client, working);

        second["status"]!.GetValue<string>().Should().Be("Success", second.ToJsonString());
        NodeRun(second, "sync")["rowCount"]!.GetValue<int>().Should().Be(1);
        Output(second, "sync")["items"]![0]!["OrderId"]!.GetValue<long>().Should().Be(2);
    }
    [Fact]
    public async Task ImportNodesFetchParseMapAndWriteRows()
    {
        var (client, connectionId, _) = await SetupAsync();
        var inbox = Directory.CreateDirectory(Path.Combine(factory.DataFolder, $"inbox-{Guid.NewGuid():N}")).FullName;
        await File.WriteAllTextAsync(Path.Combine(inbox, "orders.csv"), "order_id,customer_name\n10,Gamma\n11,Delta\n", _ct);

        var run = await RunAsync(client,
            [
                Node("start", "manual"),
                Node("fetch", "fetch", new JsonObject { ["From"] = "Local", ["Path"] = inbox, ["Pattern"] = "*.csv" }),
                Node("parse", "parse", new JsonObject { ["Format"] = "CSV" }),
                Node("map", "mapping", new JsonObject { ["Columns"] = new JsonArray(
                    new JsonObject { ["sourceColumn"] = "order_id", ["targetColumn"] = "OrderId", ["dataType"] = "int", ["isKeyColumn"] = true },
                    new JsonObject { ["sourceColumn"] = "customer_name", ["targetColumn"] = "CustomerName" }) }),
                Node("write", "write", new JsonObject { ["To"] = "Database", ["ConnectionId"] = connectionId, ["Table"] = "ImportedOrders",
                    ["Load"] = "Upsert", ["KeyColumns"] = "OrderId" })
            ],
            [Edge("start", "fetch"), Edge("fetch", "parse"), Edge("parse", "map"), Edge("map", "write")]);

        run["status"]!.GetValue<string>().Should().Be("Success", run.ToJsonString());
        Output(run, "fetch")["files"]!.AsArray().Should().ContainSingle();
        Output(run, "map")["columns"]!.AsArray().Select(c => c!["name"]!.GetValue<string>()).Should().Equal("OrderId", "CustomerName");
        NodeRun(run, "write")["rowCount"]!.GetValue<int>().Should().Be(2);
        await using var db = new SqliteConnection($"Data Source={_shopDb}");
        await db.OpenAsync(_ct);
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT CustomerName FROM ImportedOrders WHERE OrderId = 11";
        (await cmd.ExecuteScalarAsync(_ct)).Should().Be("Delta");
    }
    [Theory]
    [InlineData(100, "Failed")]
    [InlineData(50, "Success")]
    public async Task EmailNodeSendsOneMailPerRowAndAppliesTheThreshold(int threshold, string status)
    {
        var (client, connectionId, _) = await SetupAsync();
        var recipients = 0;
        await using var smtp = new FakeSmtpServer(line => line.StartsWith("RCPT", StringComparison.OrdinalIgnoreCase) && Interlocked.Increment(ref recipients) == 2 ? "550 5.1.1 mailbox unavailable" : null);
        var templateId = await EmailProfiles.IdOfAsync(await client.PostAsJsonAsync("/api/templates", new { name = $"Mail {Guid.NewGuid():N}", type = "ScribanTemplate", outputFormat = "HTML", template = "<p>{{ rows[0].Email }}</p>" }, _ct));
        var mailServerId = await EmailProfiles.IdOfAsync(await client.PostAsJsonAsync("/api/destinations", new { name = $"Mail {Guid.NewGuid():N}", type = "Email", configurationJson = System.Text.Json.JsonSerializer.Serialize(EmailProfiles.Smtp(smtp.Port)) }, _ct));

        var run = await RunAsync(client,
            [
                Node("start", "manual"),
                Node("people", "query", new JsonObject { ["ConnectionId"] = connectionId, ["Query"] = EmailProfiles.Rows(3) }),
                Node("mail", "email", new JsonObject
                {
                    ["DestinationId"] = mailServerId, ["TemplateId"] = templateId, ["RecipientsFrom"] = "Column", ["RecipientsColumn"] = "Email",
                    ["Subject"] = "Hi", ["SuccessPercent"] = threshold
                })
            ],
            [Edge("start", "people"), Edge("people", "mail")]);

        run["status"]!.GetValue<string>().Should().Be(status, run.ToJsonString());
        smtp.Messages.Should().HaveCount(2);
        NodeRun(run, "mail")["errorMessage"]!.GetValue<string>().Should().Contain("user2@example.com");
    }
    [Fact]
    public async Task DestinationRemovesItsFilesWhenALaterStepFails()
    {
        var (client, connectionId, destinationId) = await SetupAsync();

        var run = await RunAsync(client,
            [
                Node("start", "manual"),
                Node("orders", "query", new JsonObject { ["ConnectionId"] = connectionId, ["Query"] = "SELECT OrderId FROM Orders" }),
                Node("file", "destination", new JsonObject { ["DestinationId"] = destinationId, ["Format"] = "CSV", ["RemoveOnFailure"] = true }),
                Node("after", "command", new JsonObject { ["ConnectionId"] = connectionId, ["Runs"] = "Query", ["Command"] = "INSERT INTO Nowhere VALUES (1)" })
            ],
            [Edge("start", "orders"), Edge("orders", "file"), Edge("file", "after")]);

        run["status"]!.GetValue<string>().Should().Be("Failed");
        var file = Output(run, "file")["files"]![0]!["path"]!.GetValue<string>();
        File.Exists(file).Should().BeFalse();
    }
    [Fact]
    public async Task EmptyQuerySkipsTheStepsAfterIt()
    {
        var (client, connectionId, destinationId) = await SetupAsync();

        var run = await RunAsync(client,
            [
                Node("start", "manual"),
                Node("pre", "command", new JsonObject { ["ConnectionId"] = connectionId, ["Runs"] = "Query", ["Command"] = "INSERT INTO RunLog VALUES ('pre')" }),
                Node("orders", "query", new JsonObject { ["ConnectionId"] = connectionId, ["Query"] = "SELECT OrderId FROM Orders WHERE 1 = 0" }),
                Node("file", "destination", new JsonObject { ["DestinationId"] = destinationId, ["Format"] = "CSV" }),
                Node("post", "command", new JsonObject { ["ConnectionId"] = connectionId, ["Runs"] = "Query", ["Command"] = "INSERT INTO RunLog VALUES ('post')", ["SkipWhenEmpty"] = true }),
                Node("always", "command", new JsonObject { ["ConnectionId"] = connectionId, ["Runs"] = "Query", ["Command"] = "INSERT INTO RunLog VALUES ('always')" })
            ],
            [Edge("start", "pre"), Edge("pre", "orders"), Edge("orders", "file"), Edge("orders", "post"), Edge("file", "always")]);

        run["status"]!.GetValue<string>().Should().Be("Success", run.ToJsonString());
        NodeRun(run, "pre")["status"]!.GetValue<string>().Should().Be("Success");
        NodeRun(run, "file")["status"]!.GetValue<string>().Should().Be("Skipped");
        NodeRun(run, "post")["status"]!.GetValue<string>().Should().Be("Skipped");
        NodeRun(run, "always")["status"]!.GetValue<string>().Should().Be("Success");
    }

    private static List<JsonObject> NodeRuns(JsonObject run, string nodeId) =>
        [.. run["nodes"]!.AsArray().Select(n => n!.AsObject()).Where(n => n["nodeId"]!.GetValue<string>() == nodeId)];

    private async Task<List<string>> RunLogAsync()
    {
        await using var db = new SqliteConnection($"Data Source={_shopDb}");
        await db.OpenAsync(_ct);
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT Entry FROM RunLog ORDER BY rowid";
        var entries = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(_ct);
        while (await reader.ReadAsync(_ct))
            entries.Add(reader.GetString(0));
        return entries;
    }

    [Fact]
    public async Task CycleFetchesPagesUntilAnEmptyPage()
    {
        var (client, _, _) = await SetupAsync();
        using var api = new MockHttpServer();
        api.Respond = url => int.Parse(url.Query.Split('=')[1]) is var page and <= 3 ? $"[{{\"id\":{page}0}},{{\"id\":{page}1}}]" : "[]";

        var run = await RunAsync(client,
            [
                Node("start", "manual"),
                Node("page", "fetch", new JsonObject { ["From"] = "Http", ["Settings"] = new JsonObject { ["url"] = api.BaseUrl + "items?page={{ run_index + 1 }}" } }),
                Node("parse", "parse", new JsonObject { ["Format"] = "JSON" }),
                Node("more", "if", new JsonObject { ["Column"] = "id", ["Operator"] = "isNotEmpty" })
            ],
            [Edge("start", "page"), Edge("page", "parse"), Edge("parse", "more"), Edge("more", "page", "true")]);

        run["status"]!.GetValue<string>().Should().Be("Success", run.ToJsonString());
        api.RequestCount.Should().Be(4);
        NodeRuns(run, "page").Select(n => n["iteration"]!.GetValue<int>()).Should().Equal(0, 1, 2, 3);
        NodeRuns(run, "parse").Sum(n => n["rowCount"]!.GetValue<int>()).Should().Be(6);
    }

    [Fact]
    public async Task QueryReadsAValueFromAnEarlierQuery()
    {
        var (client, connectionId, _) = await SetupAsync();

        var run = await RunAsync(client,
            [
                Node("start", "manual"),
                Node("top", "query", new JsonObject { ["ConnectionId"] = connectionId, ["Query"] = "SELECT MAX(OrderId) AS Top FROM Orders" }),
                Node("customer", "query", new JsonObject { ["ConnectionId"] = connectionId, ["Query"] = "SELECT Customer FROM Orders WHERE OrderId = {{ node.top.first.Top }}" })
            ],
            [Edge("start", "top"), Edge("top", "customer")]);

        run["status"]!.GetValue<string>().Should().Be("Success", run.ToJsonString());
        Output(run, "customer")["items"]!.AsArray().Should().ContainSingle().Which!["Customer"]!.GetValue<string>().Should().Be("Delta");
    }

    [Fact]
    public async Task LoopSendsRowsInBatchesAndDoneReturnsEveryRow()
    {
        var (client, connectionId, _) = await SetupAsync();

        var run = await RunAsync(client,
            [
                Node("start", "manual"),
                Node("orders", "query", new JsonObject { ["ConnectionId"] = connectionId, ["Query"] = "SELECT OrderId FROM Orders" }),
                Node("batch", "loop", new JsonObject { ["BatchSize"] = 3 }),
                Node("log", "command", new JsonObject { ["ConnectionId"] = connectionId, ["Runs"] = "Query", ["Command"] = "INSERT INTO RunLog VALUES ({{ run_index }} || ':{rowcount}')" })
            ],
            [Edge("start", "orders"), Edge("orders", "batch"), Edge("batch", "log", "loop"), Edge("log", "batch")]);

        run["status"]!.GetValue<string>().Should().Be("Success", run.ToJsonString());
        (await RunLogAsync()).Should().Equal("0:3", "1:1");
        var passes = NodeRuns(run, "batch");
        passes.Should().HaveCount(3);
        JsonNode.Parse(passes[^1]["outputSampleJson"]!.GetValue<string>())!["done"]!["total"]!.GetValue<int>().Should().Be(4);
    }

    [Fact]
    public async Task EndlessCycleStopsAtThePassLimit()
    {
        var (client, connectionId, _) = await SetupAsync();
        JsonObject Select() => new() { ["ConnectionId"] = connectionId, ["Runs"] = "Query", ["Command"] = "SELECT 1" };

        var id = await CreateAsync(client,
            [Node("start", "manual"), Node("orders", "query", new JsonObject { ["ConnectionId"] = connectionId, ["Query"] = "SELECT OrderId FROM Orders" }), Node("a", "command", Select()), Node("b", "command", Select())],
            [Edge("start", "orders"), Edge("orders", "a"), Edge("a", "b"), Edge("b", "a")], maxPasses: 5);
        var run = await RunAgainAsync(client, id);

        run["status"]!.GetValue<string>().Should().Be("Failed");
        run["errorMessage"]!.GetValue<string>().Should().Contain("5");
        NodeRuns(run, "a").Should().HaveCount(5);
    }

    [Fact]
    public async Task RunPerRowBindsExpressionsAsSqlParameters()
    {
        var (client, connectionId, _) = await SetupAsync();
        const string attack = "x'); DROP TABLE Orders; --";
        await using (var setup = new SqliteConnection($"Data Source={_shopDb}"))
        {
            await setup.OpenAsync(_ct);
            await using var insert = setup.CreateCommand();
            insert.CommandText = "INSERT INTO Regions VALUES ($name, 'Mallory')";
            insert.Parameters.AddWithValue("$name", attack);
            await insert.ExecuteNonQueryAsync(_ct);
        }

        var run = await RunAsync(client,
            [
                Node("start", "manual"),
                Node("names", "query", new JsonObject { ["ConnectionId"] = connectionId, ["Query"] = "SELECT Region AS Name FROM Regions ORDER BY rowid" }),
                Node("log", "command", new JsonObject { ["ConnectionId"] = connectionId, ["Runs"] = "Query", ["Command"] = "INSERT INTO RunLog VALUES ({{ json.Name }})", ["RunPerRow"] = true })
            ],
            [Edge("start", "names"), Edge("names", "log")]);

        run["status"]!.GetValue<string>().Should().Be("Success", run.ToJsonString());
        (await RunLogAsync()).Should().Equal("North", "South", attack);
        await using var db = new SqliteConnection($"Data Source={_shopDb}");
        await db.OpenAsync(_ct);
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Orders";
        (await cmd.ExecuteScalarAsync(_ct)).Should().Be(4L);
    }

    [Fact]
    public async Task EmailNodeHoldsMailsForApprovalByTheConfiguredRole()
    {
        var (client, connectionId, _) = await SetupAsync();
        await using var smtp = new FakeSmtpServer(_ => null);
        var templateId = await EmailProfiles.IdOfAsync(await client.PostAsJsonAsync("/api/templates", new { name = $"Mail {Guid.NewGuid():N}", type = "ScribanTemplate", outputFormat = "HTML", template = "<p>{{ rows[0].Email }}</p>" }, _ct));
        var mailServerId = await EmailProfiles.IdOfAsync(await client.PostAsJsonAsync("/api/destinations", new { name = $"Mail {Guid.NewGuid():N}", type = "Email", configurationJson = System.Text.Json.JsonSerializer.Serialize(EmailProfiles.Smtp(smtp.Port)) }, _ct));

        var run = await RunAsync(client,
            [
                Node("start", "manual"),
                Node("people", "query", new JsonObject { ["ConnectionId"] = connectionId, ["Query"] = EmailProfiles.Rows(3) }),
                Node("mail", "email", new JsonObject
                {
                    ["DestinationId"] = mailServerId, ["TemplateId"] = templateId, ["RecipientsFrom"] = "Column", ["RecipientsColumn"] = "Email",
                    ["Subject"] = "Hi", ["RequireApproval"] = true, ["ApproverRoles"] = new JsonArray("Admin")
                })
            ],
            [Edge("start", "people"), Edge("people", "mail")]);

        run["status"]!.GetValue<string>().Should().Be("Success", run.ToJsonString());
        NodeRun(run, "mail")["errorMessage"]!.GetValue<string>().Should().Contain("3 emails wait for approval");
        smtp.Messages.Should().BeEmpty();
        var approvals = (await client.GetFromJsonAsync<JsonObject>("/api/email-approvals?status=Pending&pageSize=100", _ct))!["data"]!.AsArray()
            .Where(a => a!["workflowExecutionId"]?.GetValue<string>() == run["id"]!.GetValue<string>()).ToList();
        approvals.Should().HaveCount(3).And.OnlyContain(a => a!["nodeId"]!.GetValue<string>() == "mail");
        var approve = await client.PostAsJsonAsync($"/api/email-approvals/{approvals[0]!["guid"]!.GetValue<string>()}/approve", new { notes = "ok" }, _ct);
        approve.StatusCode.Should().Be(HttpStatusCode.OK, await approve.Content.ReadAsStringAsync(_ct));
    }
}
