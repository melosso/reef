using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using FluentAssertions;

namespace Reef.Tests.Api;

[Collection(ApiCollection.Name)]
public class RequiredNameApiTests(ReefApiFactory factory)
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/api/connections", """{ "name": " ", "type": "Sqlite", "connectionString": "", "hash": "" }""")]
    [InlineData("/api/destinations", """{ "name": " " }""")]
    [InlineData("/api/templates", """{ "name": " " }""")]
    [InlineData("/api/jobs", """{ "name": " " }""")]
    public async Task BlankNameIsRejectedWithAFieldError(string route, string body)
    {
        var admin = await factory.CreateAdminClientAsync();

        var response = await admin.PostAsync(route, new StringContent(body, System.Text.Encoding.UTF8, "application/json"), _ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonObject>(_ct))!["errors"]![0]!["field"]!.GetValue<string>().Should().Be("Name");
    }

    [Fact]
    public async Task BodyWithoutRequiredMembersIsABadRequest()
    {
        var admin = await factory.CreateAdminClientAsync();

        var response = await admin.PostAsync("/api/connections", new StringContent("""{ "name": "Shop" }""", System.Text.Encoding.UTF8, "application/json"), _ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
