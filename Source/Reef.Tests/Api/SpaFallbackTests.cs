using Microsoft.AspNetCore.Mvc.Testing;
using System.Net.Http.Json;
using System.Net;
using FluentAssertions;
using Xunit;

namespace Reef.Tests.Api;

[Collection(ApiCollection.Name)]
public class SpaFallbackTests(ReefApiFactory factory)
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private HttpClient NoRedirects() => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    private static async Task ShouldBeTheShell(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        (await response.Content.ReadAsStringAsync()).Should().Contain("<div id=\"app\">");
    }

    [Theory]
    [InlineData("/login")]
    [InlineData("/logout")]
    [InlineData("/index")]
    public async Task PublicPagesServeTheShellWithoutSignIn(string path) =>
        await ShouldBeTheShell(await NoRedirects().GetAsync(path, _ct));

    [Theory]
    [InlineData("/jobs/12", "/login?next=%2Fjobs%2F12")]
    [InlineData("/account/security", "/login?next=%2Faccount%2Fsecurity")]
    [InlineData("/executions?page=2", "/login?next=%2Fexecutions%3Fpage%3D2")]
    public async Task AppPagesRedirectSignedOutVisitorsToLogin(string path, string location)
    {
        var response = await NoRedirects().GetAsync(path, _ct);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        response.Headers.Location!.OriginalString.Should().Be(location);
    }

    [Theory]
    [InlineData("/profiles/export/42")]
    [InlineData("/admin/users")]
    [InlineData("/connections/new")]
    public async Task AppPagesServeTheShellToSignedInUsers(string path)
    {
        var (username, _) = await factory.SeededAdminAsync();
        var client = NoRedirects();
        (await client.PostAsJsonAsync("/api/auth/login", new { username, password = ReefApiFactory.AdminPassword }, _ct)).EnsureSuccessStatusCode();

        await ShouldBeTheShell(await client.GetAsync(path, _ct));
    }

    [Fact]
    public async Task UnknownApiRoutesReturnJson404()
    {
        var response = await factory.CreateClient().GetAsync("/api/does-not-exist", _ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
    }

    [Fact]
    public async Task NonGetRequestsNeverReceiveTheSpaShell()
    {
        var response = await factory.CreateClient().PostAsync("/some-page", new StringContent(""), _ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ResponsesIncludeAContentSecurityPolicy()
    {
        var response = await factory.CreateClient().GetAsync("/dashboard", _ct);

        response.Headers.GetValues("Content-Security-Policy").Single().Should().Contain("default-src 'self'");
    }
}
