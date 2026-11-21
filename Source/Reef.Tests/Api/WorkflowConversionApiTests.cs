using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Reef.Tests.Api;

[Collection(ApiCollection.Name)]
public class WorkflowConversionApiTests(ReefApiFactory factory)
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private async Task<(HttpClient Client, string ConnectionId, string DestinationId, string ShopDb)> SetupAsync()
    {
        var shopDb = Path.Combine(factory.DataFolder, $"shop-{Guid.NewGuid():N}.db");
        await using (var db = new SqliteConnection($"Data Source={shopDb}"))
        {
            await db.OpenAsync(_ct);
            await using var cmd = db.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE Orders (OrderId INTEGER PRIMARY KEY, Customer TEXT, Region TEXT);
                INSERT INTO Orders VALUES (1, 'Alpha', 'North'), (2, 'Beta', 'South'), (3, 'Gamma', 'North');
                CREATE TABLE ImportedOrders (OrderId INTEGER PRIMARY KEY, CustomerName TEXT);
                """;
            await cmd.ExecuteNonQueryAsync(_ct);
        }
        var client = await factory.CreateAdminClientAsync();
        var connectionId = await EmailProfiles.IdOfAsync(await client.PostAsJsonAsync("/api/connections",
            new { name = $"Shop {Guid.NewGuid():N}", type = "Sqlite", connectionString = $"Data Source={shopDb}", isActive = true, hash = "" }, _ct));
        var destinations = await client.GetFromJsonAsync<JsonArray>("/api/destinations", _ct);
        var destinationId = destinations!.First(d => d!["type"]!.GetValue<string>() == "Local")!["id"]!.GetValue<string>();
        return (client, connectionId, destinationId, shopDb);
    }

    private static JsonObject Step(string type, JsonObject config) => new() { ["id"] = type, ["type"] = type, ["x"] = 0, ["y"] = 0, ["config"] = config };

    private async Task<string> CreateProfileAsync(HttpClient client, string kind, params JsonObject[] steps)
    {
        var document = new JsonObject
        {
            ["kind"] = kind,
            ["settings"] = new JsonObject { ["Name"] = $"Profile {Guid.NewGuid():N}", ["IsEnabled"] = true },
            ["nodes"] = new JsonArray(steps)
        };
        var created = await client.PostAsJsonAsync($"/api/profile-documents/{kind}", document, _ct);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(_ct));
        return (await created.Content.ReadFromJsonAsync<JsonObject>(_ct))!["id"]!.GetValue<string>();
    }

    private async Task<JsonObject> ConvertAsync(HttpClient client, string kind, string id)
    {
        var converted = await client.PostAsync($"/api/workflows/convert/{kind}/{id}", null, _ct);
        converted.StatusCode.Should().Be(HttpStatusCode.Created, await converted.Content.ReadAsStringAsync(_ct));
        return (await converted.Content.ReadFromJsonAsync<JsonObject>(_ct))!;
    }

    private async Task<JsonObject> RunWorkflowAsync(HttpClient client, string workflowId)
    {
        var run = await client.PostAsync($"/api/workflows/{workflowId}/run", null, _ct);
        run.StatusCode.Should().Be(HttpStatusCode.OK, await run.Content.ReadAsStringAsync(_ct));
        var runId = (await run.Content.ReadFromJsonAsync<JsonObject>(_ct))!["id"]!.GetValue<string>();
        return (await client.GetFromJsonAsync<JsonObject>($"/api/workflow-executions/{runId}", _ct))!;
    }

    private static JsonObject NodeRun(JsonObject run, string nodeId) =>
        run["nodes"]!.AsArray().Single(n => n!["nodeId"]!.GetValue<string>() == nodeId)!.AsObject();

    private static List<string> Files(JsonObject run, string nodeId) =>
        [.. JsonNode.Parse(NodeRun(run, nodeId)["outputSampleJson"]!.GetValue<string>())!["main"]!["files"]!.AsArray().Select(f => f!["path"]!.GetValue<string>())];

    [Fact]
    public async Task ConvertedExportWritesTheSameContentAsTheProfile()
    {
        var (client, connectionId, destinationId, _) = await SetupAsync();
        var profileId = await CreateProfileAsync(client, "export",
            Step("source", new JsonObject { ["ConnectionId"] = connectionId, ["Query"] = "SELECT OrderId, Customer FROM Orders ORDER BY OrderId" }),
            Step("destination", new JsonObject { ["OutputDestinationId"] = destinationId, ["OutputFormat"] = "CSV" }));

        var legacy = await EmailProfiles.RunAsync(client, profileId);
        var converted = await ConvertAsync(client, "export", profileId);
        var run = await RunWorkflowAsync(client, converted["workflow"]!["id"]!.GetValue<string>());

        legacy["status"]!.GetValue<string>().Should().Be("Success", legacy.ToJsonString());
        run["status"]!.GetValue<string>().Should().Be("Success", run.ToJsonString());
        var workflowFile = Files(run, "destination").Single();
        var legacyFile = Path.Combine(Path.GetDirectoryName(workflowFile)!, Path.GetFileName(legacy["outputPath"]!.GetValue<string>()));
        (await File.ReadAllTextAsync(workflowFile, _ct)).Should().Be(await File.ReadAllTextAsync(legacyFile, _ct));
    }

    [Fact]
    public async Task ConvertedSplitExportWritesTheSameFilesAsTheProfile()
    {
        var (client, connectionId, destinationId, _) = await SetupAsync();
        var profileId = await CreateProfileAsync(client, "export",
            Step("source", new JsonObject { ["ConnectionId"] = connectionId, ["Query"] = "SELECT OrderId, Region FROM Orders ORDER BY OrderId" }),
            Step("splitoutput", new JsonObject { ["SplitKeyColumn"] = "Region", ["SplitFilenameTemplate"] = "{splitkey}_{guid}.{format}" }),
            Step("destination", new JsonObject { ["OutputDestinationId"] = destinationId, ["OutputFormat"] = "JSON" }));

        var converted = await ConvertAsync(client, "export", profileId);
        var run = await RunWorkflowAsync(client, converted["workflow"]!["id"]!.GetValue<string>());

        run["status"]!.GetValue<string>().Should().Be("Success", run.ToJsonString());
        Files(run, "destination").Select(f => Path.GetFileName(f).Split('_')[0]).Should().BeEquivalentTo("North", "South");
    }

    [Fact]
    public async Task ConversionMovesSmartSyncHistoryToTheWorkflow()
    {
        var (client, connectionId, destinationId, _) = await SetupAsync();
        var profileId = await CreateProfileAsync(client, "export",
            Step("source", new JsonObject { ["ConnectionId"] = connectionId, ["Query"] = "SELECT OrderId, Customer FROM Orders" }),
            Step("deltasync", new JsonObject { ["DeltaSyncReefIdColumn"] = "OrderId" }),
            Step("destination", new JsonObject { ["OutputDestinationId"] = destinationId, ["OutputFormat"] = "JSON" }));
        (await EmailProfiles.RunAsync(client, profileId))["rowCount"]!.GetValue<int>().Should().Be(3);

        var converted = await ConvertAsync(client, "export", profileId);
        var run = await RunWorkflowAsync(client, converted["workflow"]!["id"]!.GetValue<string>());

        run["status"]!.GetValue<string>().Should().Be("Success", run.ToJsonString());
        NodeRun(run, "deltasync")["rowCount"]!.GetValue<int>().Should().Be(0);
        NodeRun(run, "destination")["status"]!.GetValue<string>().Should().Be("Skipped");
    }

    [Fact]
    public async Task ConvertedImportWritesRowsAndExportCopies()
    {
        var (client, connectionId, destinationId, shopDb) = await SetupAsync();
        var inbox = Directory.CreateDirectory(Path.Combine(factory.DataFolder, $"inbox-{Guid.NewGuid():N}")).FullName;
        await File.WriteAllTextAsync(Path.Combine(inbox, "orders.csv"), "order_id,customer_name\n10,Gamma\n11,Delta\n", _ct);
        var profileId = await CreateProfileAsync(client, "import",
            Step("source", new JsonObject { ["SourceType"] = "Local", ["SourceFilePath"] = inbox, ["SourceFilePattern"] = "*.csv" }),
            Step("parse", new JsonObject { ["SourceFormat"] = "CSV" }),
            Step("mapping", new JsonObject { ["ColumnMappingsJson"] = new JsonArray(
                new JsonObject { ["sourceColumn"] = "order_id", ["targetColumn"] = "OrderId", ["isKeyColumn"] = true },
                new JsonObject { ["sourceColumn"] = "customer_name", ["targetColumn"] = "CustomerName" }) }),
            Step("target", new JsonObject { ["TargetType"] = "Database", ["TargetConnectionId"] = connectionId, ["TargetTable"] = "ImportedOrders", ["LoadStrategy"] = "Upsert", ["UpsertKeyColumns"] = "OrderId" }),
            Step("outputs", new JsonObject { ["OutputTargets"] = new JsonArray(new JsonObject { ["DestinationId"] = destinationId, ["OutputFormat"] = "CSV" }) }));

        var converted = await ConvertAsync(client, "import", profileId);
        var run = await RunWorkflowAsync(client, converted["workflow"]!["id"]!.GetValue<string>());

        run["status"]!.GetValue<string>().Should().Be("Success", run.ToJsonString());
        NodeRun(run, "write")["rowCount"]!.GetValue<int>().Should().Be(2);
        run["nodes"]!.AsArray().Should().Contain(n => n!["nodeId"]!.GetValue<string>().StartsWith("copy") && n["status"]!.GetValue<string>() == "Success");
        await using var db = new SqliteConnection($"Data Source={shopDb}");
        await db.OpenAsync(_ct);
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT CustomerName FROM ImportedOrders WHERE OrderId = 11";
        (await cmd.ExecuteScalarAsync(_ct)).Should().Be("Delta");
    }

    [Fact]
    public async Task ConversionReportsSettingsWithoutAWorkflowEquivalent()
    {
        var (client, connectionId, destinationId, _) = await SetupAsync();
        var profileId = await CreateProfileAsync(client, "export",
            Step("source", new JsonObject { ["ConnectionId"] = connectionId, ["Query"] = "SELECT OrderId, Region FROM Orders" }),
            Step("splitoutput", new JsonObject { ["SplitKeyColumn"] = "Region", ["PostProcessPerSplit"] = true }),
            Step("destination", new JsonObject { ["OutputDestinationId"] = destinationId, ["OutputFormat"] = "JSON" }),
            Step("postprocess", new JsonObject { ["PostProcessType"] = "Query", ["PostProcessConfig"] = new JsonObject { ["Command"] = "SELECT 1" } }));

        var converted = await ConvertAsync(client, "export", profileId);

        converted["notes"]!.AsArray().Select(n => n!.GetValue<string>()).Should().Contain(n => n.Contains("every split file"));
    }

    [Fact]
    public async Task ConvertedProfileKeepsItsScheduleAndWebhookUrl()
    {
        var (client, connectionId, destinationId, _) = await SetupAsync();
        var profileId = await CreateProfileAsync(client, "export",
            Step("source", new JsonObject { ["ConnectionId"] = connectionId, ["Query"] = "SELECT OrderId FROM Orders" }),
            Step("destination", new JsonObject { ["OutputDestinationId"] = destinationId, ["OutputFormat"] = "CSV" }));
        (await client.PostAsJsonAsync("/api/jobs", new
        {
            name = $"Nightly {Guid.NewGuid():N}", type = "ProfileExecution", profileId, scheduleType = "Weekly", startTime = "06:30:00", weekDays = "0,4", isEnabled = true
        }, _ct)).StatusCode.Should().Be(HttpStatusCode.Created);
        var hook = await client.PostAsJsonAsync("/api/webhooks", new { profileId }, _ct);
        hook.StatusCode.Should().Be(HttpStatusCode.OK, await hook.Content.ReadAsStringAsync(_ct));
        var created = (await hook.Content.ReadFromJsonAsync<JsonObject>(_ct))!;
        var token = created["token"]!.GetValue<string>();

        var workflow = (await ConvertAsync(client, "export", profileId))["workflow"]!;

        workflow["active"]!.GetValue<bool>().Should().BeTrue();
        var nodes = workflow["nodes"]!.AsArray();
        nodes.Should().Contain(n => n!["type"]!.GetValue<string>() == "schedule" && n["config"]!["Cron"]!.GetValue<string>() == "30 6 * * 1,5");
        var hookNode = nodes.Single(n => n!["type"]!.GetValue<string>() == "webhook")!["id"]!.GetValue<string>();
        workflow["edges"]!.AsArray().Should().Contain(e => e!["from"]!.GetValue<string>() == hookNode);
        (await client.DeleteAsync($"/api/webhooks/{created["id"]!.GetValue<string>()}", _ct)).IsSuccessStatusCode.Should().BeTrue();

        var call = await factory.CreateClient().PostAsJsonAsync($"/webhooks/{token}", new { }, _ct);

        call.StatusCode.Should().Be(HttpStatusCode.OK, await call.Content.ReadAsStringAsync(_ct));
        (await call.Content.ReadFromJsonAsync<JsonObject>(_ct))!["executionId"].Should().NotBeNull();
    }
}
