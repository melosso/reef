using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Reef.Tests.Api;

[Collection(ApiCollection.Name)]
public class PublicIdApiTests(ReefApiFactory factory)
{
    private static readonly Regex Ulid = new("^[0-9A-HJKMNP-TV-Z]{26}$");
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private async Task<string> CreateConnectionAsync(HttpClient admin)
    {
        var shopDb = Path.Combine(factory.DataFolder, $"ids-{Guid.NewGuid():N}.db");
        var created = await admin.PostAsJsonAsync("/api/connections", new { name = $"Ids {Guid.NewGuid():N}", type = "Sqlite", connectionString = $"Data Source={shopDb}", isActive = true, hash = "" }, _ct);
        created.EnsureSuccessStatusCode();
        return (await created.Content.ReadFromJsonAsync<JsonObject>(_ct))!["id"]!.GetValue<string>();
    }

    [Fact]
    public async Task ResourcesAreAddressedByUlidOnly()
    {
        var admin = await factory.CreateAdminClientAsync();
        var id = await CreateConnectionAsync(admin);

        id.Should().MatchRegex(Ulid.ToString());
        (await admin.GetAsync($"/api/connections/{id}", _ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.GetAsync("/api/connections/1", _ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.GetAsync("/api/connections/01ARZ3NDEKTSV4RRFFQ69G5FAV", _ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task FiltersRejectIntegerIds()
    {
        var admin = await factory.CreateAdminClientAsync();

        (await admin.GetAsync("/api/executions?profileId=1", _ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public void NoRouteTakesAnIntegerId()
    {
        var routes = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>();

        var offenders = routes.SelectMany(route => route.RoutePattern.Parameters
                .Where(p => p.ParameterPolicies.Any(policy => policy.Content == "int")
                    || ((p.Name.EndsWith("id", StringComparison.OrdinalIgnoreCase) || p.Name == "tid")
                        && !p.ParameterPolicies.Any(policy => policy.Content == "guid" || policy.Content?.StartsWith("ulid(") == true)))
                .Select(p => $"{route.RoutePattern.RawText} {p.Name}"))
            .Distinct()
            .ToList();

        offenders.Should().BeEmpty();
    }

    [Fact]
    public async Task ResponsesNeverCarryIntegerIds()
    {
        var admin = await factory.CreateAdminClientAsync();
        await SeedAsync(admin);
        var routes = factory.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(r => r.RoutePattern.Parameters.Count == 0 && r.RoutePattern.RawText?.StartsWith("/api/") == true
                && r.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("GET") == true
                && !r.RoutePattern.RawText.StartsWith("/api/auth/oidc") && r.RoutePattern.RawText != "/api/admin/version")
            .Select(r => r.RoutePattern.RawText!)
            .Distinct();

        var leaks = new List<string>();
        foreach (var route in routes)
        {
            var response = await admin.GetAsync(route, _ct);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentType?.MediaType != "application/json") continue;
            Scan(route, JsonNode.Parse(await response.Content.ReadAsStringAsync(_ct)), leaks);
        }

        leaks.Should().BeEmpty();
    }

    private async Task SeedAsync(HttpClient admin)
    {
        var connectionId = await CreateConnectionAsync(admin);
        (await admin.PostAsJsonAsync("/api/groups", new { name = $"Ids {Guid.NewGuid():N}", description = "" }, _ct)).EnsureSuccessStatusCode();
        var destinations = await admin.GetFromJsonAsync<JsonArray>("/api/destinations", _ct);
        var destinationId = destinations!.First(d => d!["type"]!.GetValue<string>() == "Local")!["id"]!.GetValue<string>();
        var document = new JsonObject
        {
            ["kind"] = "export",
            ["settings"] = new JsonObject { ["Name"] = $"Ids {Guid.NewGuid():N}", ["IsEnabled"] = true },
            ["nodes"] = new JsonArray(
                new JsonObject { ["id"] = "source", ["type"] = "source", ["x"] = 0, ["y"] = 0, ["config"] = new JsonObject { ["ConnectionId"] = connectionId, ["Query"] = "SELECT 1 AS One" } },
                new JsonObject { ["id"] = "destination", ["type"] = "destination", ["x"] = 300, ["y"] = 0, ["config"] = new JsonObject { ["OutputDestinationId"] = destinationId, ["OutputFormat"] = "JSON" } })
        };
        var created = await admin.PostAsJsonAsync("/api/profile-documents/export", document, _ct);
        created.EnsureSuccessStatusCode();
        var profileId = (await created.Content.ReadFromJsonAsync<JsonObject>(_ct))!["id"]!.GetValue<string>();
        (await admin.PostAsJsonAsync($"/api/executions/execute/{profileId}", new { }, _ct)).EnsureSuccessStatusCode();
    }

    private static void Scan(string path, JsonNode? node, List<string> leaks)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (name, value) in obj)
                {
                    var looksLikeId = name.Equals("id", StringComparison.OrdinalIgnoreCase) || name.EndsWith("Id") || name.EndsWith("Ids");
                    if (looksLikeId && value is JsonValue v && v.GetValueKind() == JsonValueKind.Number)
                        leaks.Add($"{path}.{name}");
                    if (looksLikeId && value is JsonArray a && a.Any(x => x is JsonValue xv && xv.GetValueKind() == JsonValueKind.Number))
                        leaks.Add($"{path}.{name}[]");
                    Scan($"{path}.{name}", value, leaks);
                }
                break;
            case JsonArray array:
                foreach (var item in array) Scan($"{path}[]", item, leaks);
                break;
        }
    }
}
