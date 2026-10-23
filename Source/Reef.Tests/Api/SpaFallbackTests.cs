using System.Net;
using FluentAssertions;
using Xunit;

namespace Reef.Tests.Api;

[Collection(ApiCollection.Name)]
public class SpaFallbackTests(ReefApiFactory factory)
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("/index")]
    [InlineData("/profiles/export/42")]
    [InlineData("/admin?tab=users")]
    public async Task App_routes_serve_the_spa_shell(string path)
    {
        var response = await factory.CreateClient().GetAsync(path, _ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        (await response.Content.ReadAsStringAsync(_ct)).Should().Contain("<div id=\"app\">");
    }

    [Fact]
    public async Task Unknown_api_routes_return_json_404()
    {
        var response = await factory.CreateClient().GetAsync("/api/does-not-exist", _ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task Non_get_requests_never_receive_the_spa_shell()
    {
        var response = await factory.CreateClient().PostAsync("/some-page", new StringContent(""), _ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
