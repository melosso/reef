using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Reef.Tests.Api;

public class ProfileDocumentsApiTests(ReefApiFactory factory) : IClassFixture<ReefApiFactory>
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private async Task<(HttpClient Client, int ConnectionId, string ShopDb)> SetupAsync()
    {
        var shopDb = Path.Combine(factory.DataFolder, $"shop-{Guid.NewGuid():N}.db");
        await using (var db = new SqliteConnection($"Data Source={shopDb}"))
        {
            await db.OpenAsync(_ct);
            await using var cmd = db.CreateCommand();
            cmd.CommandText = "CREATE TABLE Orders (OrderId INTEGER PRIMARY KEY, Customer TEXT); INSERT INTO Orders VALUES (1, 'Alpha'), (2, 'Beta'); CREATE TABLE ImportedOrders (OrderId INTEGER PRIMARY KEY, CustomerName TEXT);";
            await cmd.ExecuteNonQueryAsync(_ct);
        }
        var client = await factory.CreateAdminClientAsync();
        var created = await client.PostAsJsonAsync("/api/connections", new { name = $"Shop {Guid.NewGuid():N}", type = "Sqlite", connectionString = $"Data Source={shopDb}", isActive = true, hash = "" }, _ct);
        created.EnsureSuccessStatusCode();
        var connectionId = (await created.Content.ReadFromJsonAsync<JsonObject>(_ct))!["id"]!.GetValue<int>();
        return (client, connectionId, shopDb);
    }

    private static JsonObject Node(string type, JsonObject config) => new() { ["id"] = type, ["type"] = type, ["x"] = 40, ["y"] = 40, ["config"] = config };

    [Fact]
    public async Task Export_document_saves_and_reads_back_with_positions()
    {
        var (client, connectionId, _) = await SetupAsync();
        var destinations = await client.GetFromJsonAsync<JsonArray>("/api/destinations", _ct);
        var destinationId = destinations!.First(d => d!["type"]!.GetValue<string>() == "Local")!["id"]!.GetValue<int>();
        var document = new JsonObject
        {
            ["kind"] = "export",
            ["settings"] = new JsonObject { ["Name"] = $"Export {Guid.NewGuid():N}", ["IsEnabled"] = true },
            ["nodes"] = new JsonArray(
                Node("source", new JsonObject { ["ConnectionId"] = connectionId, ["Query"] = "SELECT * FROM Orders" }),
                Node("destination", new JsonObject { ["OutputDestinationId"] = destinationId, ["OutputFormat"] = "XML" }))
        };
        document["nodes"]![1]!["x"] = 400;

        var created = await client.PostAsJsonAsync("/api/profile-documents/export", document, _ct);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var saved = await created.Content.ReadFromJsonAsync<JsonObject>(_ct);
        var read = await client.GetFromJsonAsync<JsonObject>($"/api/profile-documents/export/{saved!["id"]}", _ct);

        var destination = read!["nodes"]!.AsArray().Single(n => n!["type"]!.GetValue<string>() == "destination")!;
        destination["x"]!.GetValue<double>().Should().Be(400);
        destination["config"]!["OutputFormat"]!.GetValue<string>().Should().Be("XML");
    }

    [Fact]
    public async Task Invalid_document_returns_errors_per_node_and_field()
    {
        var (client, _, _) = await SetupAsync();
        var document = new JsonObject
        {
            ["kind"] = "export",
            ["settings"] = new JsonObject { ["Name"] = "" },
            ["nodes"] = new JsonArray(Node("source", new JsonObject()), Node("emailexport", new JsonObject()), Node("destination", new JsonObject()))
        };

        var response = await client.PostAsJsonAsync("/api/profile-documents/export", document, _ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var errors = (await response.Content.ReadFromJsonAsync<JsonObject>(_ct))!["errors"]!.AsArray();
        errors.Should().Contain(e => e!["field"] != null && e["field"]!.GetValue<string>() == "Name");
        errors.Should().Contain(e => e!["nodeId"] != null && e["nodeId"]!.GetValue<string>() == "source" && e["field"]!.GetValue<string>() == "Query");
        errors.Should().Contain(e => e!["message"]!.GetValue<string>().Contains("cannot be combined"));
    }

    [Fact]
    public async Task Import_document_runs_with_column_mapping_and_export_copies()
    {
        var (client, connectionId, shopDb) = await SetupAsync();
        var inbox = Directory.CreateDirectory(Path.Combine(factory.DataFolder, $"in-{Guid.NewGuid():N}")).FullName;
        await File.WriteAllTextAsync(Path.Combine(inbox, "orders.csv"), "order_id,customer_name\n10,Gamma\n11,Delta\n", _ct);
        var destinations = await client.GetFromJsonAsync<JsonArray>("/api/destinations", _ct);
        var destinationId = destinations!.First(d => d!["type"]!.GetValue<string>() == "Local")!["id"]!.GetValue<int>();
        var name = $"Import {Guid.NewGuid():N}";
        var document = new JsonObject
        {
            ["kind"] = "import",
            ["settings"] = new JsonObject { ["Name"] = name, ["IsEnabled"] = true },
            ["nodes"] = new JsonArray(
                Node("source", new JsonObject { ["SourceType"] = "Local", ["SourceFilePath"] = inbox, ["SourceFilePattern"] = "*.csv" }),
                Node("parse", new JsonObject { ["SourceFormat"] = "CSV" }),
                Node("mapping", new JsonObject { ["ColumnMappingsJson"] = new JsonArray(
                    new JsonObject { ["sourceColumn"] = "order_id", ["targetColumn"] = "OrderId", ["isKeyColumn"] = true },
                    new JsonObject { ["sourceColumn"] = "customer_name", ["targetColumn"] = "CustomerName" }) }),
                Node("target", new JsonObject { ["TargetType"] = "Database", ["TargetConnectionId"] = connectionId, ["TargetTable"] = "ImportedOrders", ["LoadStrategy"] = "Upsert", ["UpsertKeyColumns"] = "OrderId" }),
                Node("outputs", new JsonObject { ["OutputTargets"] = new JsonArray(new JsonObject { ["DestinationId"] = destinationId, ["OutputFormat"] = "JSON" }) }))
        };

        var created = await client.PostAsJsonAsync("/api/profile-documents/import", document, _ct);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await created.Content.ReadFromJsonAsync<JsonObject>(_ct))!["id"]!.GetValue<int>();

        var duplicate = await client.PostAsJsonAsync("/api/profile-documents/import", document, _ct);
        duplicate.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var read = await client.GetFromJsonAsync<JsonObject>($"/api/profile-documents/import/{id}", _ct);
        var copies = read!["nodes"]!.AsArray().Single(n => n!["type"]!.GetValue<string>() == "outputs")!["config"]!["OutputTargets"]!.AsArray();
        copies.Should().ContainSingle().Which!["Id"]!.GetValue<int>().Should().BePositive();

        var run = await client.PostAsJsonAsync($"/api/profiles/{id}/execute?type=import", new { }, _ct);
        var result = await run.Content.ReadFromJsonAsync<JsonObject>(_ct);
        result!["rowsInserted"]!.GetValue<int>().Should().Be(2, result.ToJsonString());

        await using var db = new SqliteConnection($"Data Source={shopDb}");
        await db.OpenAsync(_ct);
        await using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT CustomerName FROM ImportedOrders WHERE OrderId = 11";
        (await cmd.ExecuteScalarAsync(_ct)).Should().Be("Delta");
    }

    [Fact]
    public async Task Legacy_import_update_keeps_canvas_positions()
    {
        var (client, connectionId, _) = await SetupAsync();
        var document = new JsonObject
        {
            ["kind"] = "import",
            ["settings"] = new JsonObject { ["Name"] = $"Layout {Guid.NewGuid():N}", ["IsEnabled"] = true },
            ["nodes"] = new JsonArray(
                Node("source", new JsonObject { ["SourceType"] = "Local", ["SourceFilePath"] = factory.DataFolder }),
                Node("parse", new JsonObject { ["SourceFormat"] = "CSV" }),
                Node("target", new JsonObject { ["TargetType"] = "Database", ["TargetConnectionId"] = connectionId, ["TargetTable"] = "ImportedOrders", ["LoadStrategy"] = "Append" }))
        };
        document["nodes"]![2]!["x"] = 640;
        var created = await client.PostAsJsonAsync("/api/profile-documents/import", document, _ct);
        var id = (await created.Content.ReadFromJsonAsync<JsonObject>(_ct))!["id"]!.GetValue<int>();

        var legacy = await client.GetFromJsonAsync<JsonObject>($"/api/profiles/{id}?type=import", _ct);
        legacy!.Remove("canvasLayoutJson");
        legacy["batchSize"] = 250;
        (await client.PutAsJsonAsync($"/api/profiles/{id}?type=import", legacy, _ct)).EnsureSuccessStatusCode();

        var read = await client.GetFromJsonAsync<JsonObject>($"/api/profile-documents/import/{id}", _ct);
        read!["nodes"]!.AsArray().Single(n => n!["type"]!.GetValue<string>() == "target")!["x"]!.GetValue<double>().Should().Be(640);
    }
}
