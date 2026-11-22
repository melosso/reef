using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Dapper;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Reef.Core.Services;
using Reef.Legacy.Conversion;
using Xunit;

namespace Reef.Tests.Api;

[Collection(ApiCollection.Name)]
public class ProfileCutoverApiTests(ReefApiFactory factory)
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private SqliteConnection ReefDb() => new($"Data Source={Path.Combine(factory.DataFolder, "Reef.db")};Pooling=False");

    private async Task<(HttpClient Client, long ConnectionId, long DestinationId, string ShopDb)> SetupAsync()
    {
        var shopDb = Path.Combine(factory.DataFolder, $"shop-{Guid.NewGuid():N}.db");
        await using (var shop = new SqliteConnection($"Data Source={shopDb};Pooling=False"))
            await shop.ExecuteAsync("""
                CREATE TABLE Orders (OrderId INTEGER PRIMARY KEY, Customer TEXT, Region TEXT);
                INSERT INTO Orders VALUES (1, 'Alpha', 'North'), (2, 'Beta', 'South'), (3, 'Gamma', 'North');
                CREATE TABLE ImportedOrders (OrderId INTEGER PRIMARY KEY, CustomerName TEXT);
                """);
        var client = await factory.CreateAdminClientAsync();
        var connection = await EmailProfiles.IdOfAsync(await client.PostAsJsonAsync("/api/connections",
            new { name = $"Shop {Guid.NewGuid():N}", type = "Sqlite", connectionString = $"Data Source={shopDb}", isActive = true, hash = "" }, _ct));
        await using var db = ReefDb();
        var connectionId = await db.ExecuteScalarAsync<long>("SELECT Id FROM Connections WHERE Ulid = @connection", new { connection });
        var destinationId = await db.ExecuteScalarAsync<long>("SELECT Id FROM Destinations WHERE Type = 1 ORDER BY Id LIMIT 1");
        await db.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS Profiles (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, ConnectionId INTEGER, Query TEXT, IsEnabled INTEGER NOT NULL DEFAULT 1,
                OutputDestinationId INTEGER, OutputFormat TEXT, SplitEnabled INTEGER NOT NULL DEFAULT 0, SplitKeyColumn TEXT, SplitFilenameTemplate TEXT,
                DeltaSyncEnabled INTEGER NOT NULL DEFAULT 0, DeltaSyncReefIdColumn TEXT, EmailApprovalRoles TEXT);
            CREATE TABLE IF NOT EXISTS ProfileExecutions (Id INTEGER PRIMARY KEY AUTOINCREMENT, ProfileId INTEGER);
            CREATE TABLE IF NOT EXISTS ImportProfiles (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT NOT NULL, IsEnabled INTEGER NOT NULL DEFAULT 1, SourceType TEXT, SourceFilePath TEXT,
                SourceFilePattern TEXT, SourceFormat TEXT, ColumnMappingsJson TEXT, TargetType TEXT, TargetConnectionId INTEGER, TargetTable TEXT, LoadStrategy TEXT, UpsertKeyColumns TEXT);
            CREATE TABLE IF NOT EXISTS ImportOutputTargets (Id INTEGER PRIMARY KEY AUTOINCREMENT, ImportProfileId INTEGER, DestinationId INTEGER, OutputFormat TEXT, IsEnabled INTEGER NOT NULL DEFAULT 1, SortOrder INTEGER NOT NULL DEFAULT 0);
            """);
        return (client, connectionId, destinationId, shopDb);
    }

    private async Task<long> InsertAsync(string sql, object values)
    {
        await using var db = ReefDb();
        return await db.ExecuteScalarAsync<long>(sql + " RETURNING Id", values);
    }

    private async Task<JsonObject> WorkflowAsync(HttpClient client, string name) =>
        (await client.GetFromJsonAsync<JsonArray>("/api/workflows", _ct))!.Single(w => w!["name"]!.GetValue<string>() == name)!.AsObject();

    private Task<int> CutoverAsync()
    {
        using var scope = factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ProfileCutover>().RunAsync(_ct);
    }

    private static List<string> Files(JsonObject run, string nodeId) =>
        [.. JsonNode.Parse(TestWorkflows.NodeRun(run, nodeId)!["outputSampleJson"]!.GetValue<string>())!["main"]!["files"]!.AsArray().Select(f => f!["path"]!.GetValue<string>())];

    [Fact]
    public async Task CutoverConvertsExportsKeepsTriggersAndDropsProfileTables()
    {
        var (client, connectionId, destinationId, _) = await SetupAsync();
        var plain = $"Plain {Guid.NewGuid():N}";
        var split = $"Split {Guid.NewGuid():N}";
        var plainId = await InsertAsync("INSERT INTO Profiles (Name, ConnectionId, Query, OutputDestinationId, OutputFormat) VALUES (@plain, @connectionId, 'SELECT OrderId, Customer FROM Orders ORDER BY OrderId', @destinationId, 'CSV')",
            new { plain, connectionId, destinationId });
        await InsertAsync("INSERT INTO Profiles (Name, ConnectionId, Query, OutputDestinationId, OutputFormat, SplitEnabled, SplitKeyColumn, SplitFilenameTemplate) VALUES (@split, @connectionId, 'SELECT OrderId, Region FROM Orders', @destinationId, 'JSON', 1, 'Region', '{splitkey}_{guid}.{format}')",
            new { split, connectionId, destinationId });
        await InsertAsync("INSERT INTO Jobs (Name, Type, ProfileId, ScheduleType, StartTime, WeekDays, IsEnabled, Hash, CreatedAt) VALUES (@name, 1, @plainId, 4, '06:30:00', '0,4', 1, '', datetime('now'))",
            new { name = $"Nightly {Guid.NewGuid():N}", plainId });
        var token = WebhookService.GenerateWebhookToken();
        await InsertAsync("INSERT INTO WebhookTriggers (ProfileId, Token, IsActive, CreatedAt) VALUES (@plainId, @hash, 1, datetime('now'))", new { plainId, hash = WebhookService.ComputeTokenHash(token) });
        var approvalId = await InsertAsync("INSERT INTO PendingEmailApprovals (Guid, ProfileId, ProfileExecutionId, Recipients, Subject, HtmlBody, Status, CreatedAt, Hash) VALUES (@guid, @plainId, 1, 'a@example.com', 'Hi', '<p>x</p>', 'Pending', datetime('now'), 'h')",
            new { guid = Guid.NewGuid().ToString(), plainId });
        await using (var db = ReefDb())
            await db.ExecuteAsync("UPDATE Profiles SET EmailApprovalRoles = '[\"Admin\"]' WHERE Id = @plainId", new { plainId });

        (await CutoverAsync()).Should().BeGreaterThanOrEqualTo(2);

        var converted = await WorkflowAsync(client, plain);
        converted["active"]!.GetValue<bool>().Should().BeTrue();
        converted["nodes"]!.AsArray().Should().Contain(n => n!["type"]!.GetValue<string>() == "schedule" && n["config"]!["Cron"]!.GetValue<string>() == "30 6 * * 1,5");
        var run = await TestWorkflows.RunAsync(client, converted["id"]!.GetValue<string>());
        run["status"]!.GetValue<string>().Should().Be("Success", run.ToJsonString());
        (await File.ReadAllLinesAsync(Files(run, "destination").Single(), _ct)).Where(l => l.Length > 0).Should().HaveCount(4);
        var splitRun = await TestWorkflows.RunAsync(client, (await WorkflowAsync(client, split))["id"]!.GetValue<string>());
        Files(splitRun, "destination").Select(f => Path.GetFileName(f).Split('_')[0]).Should().BeEquivalentTo("North", "South");
        (await factory.CreateClient().PostAsJsonAsync($"/webhooks/{token}", new { }, _ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        await using var check = ReefDb();
        (await check.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM sqlite_master WHERE name IN ('Profiles', 'ImportProfiles', 'ProfileExecutions')")).Should().Be(0);
        var approval = await check.QuerySingleAsync("SELECT ProfileId, DestinationId, ApproverRoles FROM PendingEmailApprovals WHERE Id = @approvalId", new { approvalId });
        ((object?)approval.ProfileId).Should().BeNull();
        ((long?)approval.DestinationId).Should().Be(destinationId);
        ((string)approval.ApproverRoles).Should().Contain("Admin");
        Directory.GetFiles(factory.DataFolder, "Reef.pre-workflows-*.db").Should().NotBeEmpty();
        (await CutoverAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CutoverMovesSmartSyncHistoryAndConvertsImports()
    {
        var (client, connectionId, destinationId, shopDb) = await SetupAsync();
        var synced = $"Synced {Guid.NewGuid():N}";
        var imported = $"Import {Guid.NewGuid():N}";
        var syncedId = await InsertAsync("INSERT INTO Profiles (Name, ConnectionId, Query, OutputDestinationId, OutputFormat, DeltaSyncEnabled, DeltaSyncReefIdColumn) VALUES (@synced, @connectionId, 'SELECT OrderId, Customer FROM Orders', @destinationId, 'JSON', 1, 'OrderId')",
            new { synced, connectionId, destinationId });
        await using (var db = ReefDb())
        {
            foreach (var id in new[] { "1", "2", "3" })
                await db.ExecuteAsync("INSERT INTO DeltaSyncState (ProfileId, ReefId, RowHash, FirstSeenAt, LastSeenAt, IsDeleted) VALUES (@syncedId, @id, 'h', datetime('now'), datetime('now'), 0)", new { syncedId, id });
        }
        var inbox = Directory.CreateDirectory(Path.Combine(factory.DataFolder, $"inbox-{Guid.NewGuid():N}")).FullName;
        await File.WriteAllTextAsync(Path.Combine(inbox, "orders.csv"), "order_id,customer_name\n10,Gamma\n11,Delta\n", _ct);
        var importId = await InsertAsync("""
            INSERT INTO ImportProfiles (Name, SourceType, SourceFilePath, SourceFilePattern, SourceFormat, ColumnMappingsJson, TargetType, TargetConnectionId, TargetTable, LoadStrategy, UpsertKeyColumns)
            VALUES (@imported, 'Local', @inbox, '*.csv', 'CSV', '[{"sourceColumn":"order_id","targetColumn":"OrderId","isKeyColumn":true},{"sourceColumn":"customer_name","targetColumn":"CustomerName"}]', 'Database', @connectionId, 'ImportedOrders', 'Upsert', 'OrderId')
            """, new { imported, inbox, connectionId });
        await InsertAsync("INSERT INTO ImportOutputTargets (ImportProfileId, DestinationId, OutputFormat) VALUES (@importId, @destinationId, 'CSV')", new { importId, destinationId });

        await CutoverAsync();

        var syncedWorkflow = (await WorkflowAsync(client, synced))["id"]!.GetValue<string>();
        await using (var db = ReefDb())
        {
            var internalId = await db.ExecuteScalarAsync<int>("SELECT Id FROM Workflows WHERE Ulid = @syncedWorkflow", new { syncedWorkflow });
            (await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM DeltaSyncState WHERE Scope = @scope", new { scope = global::Reef.Core.Workflows.Engine.Executors.DeltaSyncNodeExecutor.Scope(internalId, "deltasync") })).Should().Be(3);
            (await db.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM DeltaSyncState WHERE ProfileId IS NOT NULL")).Should().Be(0);
        }
        var importRun = await TestWorkflows.RunAsync(client, (await WorkflowAsync(client, imported))["id"]!.GetValue<string>());
        importRun["status"]!.GetValue<string>().Should().Be("Success", importRun.ToJsonString());
        TestWorkflows.NodeRun(importRun, "write")!["rowCount"]!.GetValue<int>().Should().Be(2);
        importRun["nodes"]!.AsArray().Should().Contain(n => n!["nodeId"]!.GetValue<string>().StartsWith("copy") && n["status"]!.GetValue<string>() == "Success");
        await using var shop = new SqliteConnection($"Data Source={shopDb};Pooling=False");
        (await shop.ExecuteScalarAsync<string>("SELECT CustomerName FROM ImportedOrders WHERE OrderId = 11")).Should().Be("Delta");
    }
}
