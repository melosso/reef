using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Reef.Tests.Api;

[Collection(ApiCollection.Name)]
public class AuthApiTests(ReefApiFactory factory)
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private async Task<HttpResponseMessage> LoginAsync(string username, string password) =>
        await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { username, password }, _ct);

    [Fact]
    public async Task SignInAcceptsTheAccountEmailAddress()
    {
        var admin = await factory.CreateAdminClientAsync();
        var name = $"user{Guid.NewGuid():N}"[..16];
        (await admin.PostAsJsonAsync("/api/admin/users", new { username = name, password = "Secret-Pass-123", role = "User" }, _ct)).EnsureSuccessStatusCode();

        var login = await LoginAsync(name, "Secret-Pass-123");
        login.EnsureSuccessStatusCode();
        var token = (await login.Content.ReadFromJsonAsync<JsonObject>(_ct))!["token"]!.GetValue<string>();
        var user = factory.CreateClient();
        user.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        (await user.PutAsJsonAsync("/api/account/profile", new { displayName = name, email = $"{name}@Example.com" }, _ct)).EnsureSuccessStatusCode();

        (await LoginAsync($"{name}@example.com", "Secret-Pass-123")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await LoginAsync($"{name}@example.com", "wrong-password")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task FirstAdminGetsARandomAccountFromTheLogAndMustChangeThePassword()
    {
        var (username, changeRequired) = await factory.SeededAdminAsync();

        username.Should().MatchRegex(@"^admin-[a-z0-9]{8}@reef\.local$");
        changeRequired.Should().BeTrue();
        (await LoginAsync("admin@reef.local", "admin123")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task EmailAlreadyUsedByAnotherAccountIsRejected()
    {
        var admin = await factory.CreateAdminClientAsync();
        var (adminName, _) = await factory.SeededAdminAsync();
        var name = $"user{Guid.NewGuid():N}"[..16];
        (await admin.PostAsJsonAsync("/api/admin/users", new { username = name, password = "Secret-Pass-123", role = "User" }, _ct)).EnsureSuccessStatusCode();
        var login = await LoginAsync(name, "Secret-Pass-123");
        var user = factory.CreateClient();
        user.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await login.Content.ReadFromJsonAsync<JsonObject>(_ct))!["token"]!.GetValue<string>());

        (await admin.PutAsJsonAsync("/api/account/profile", new { email = $"{name}-taken@example.com" }, _ct)).EnsureSuccessStatusCode();

        (await user.PutAsJsonAsync("/api/account/profile", new { email = $"{name}-TAKEN@example.com" }, _ct)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await user.PutAsJsonAsync("/api/account/profile", new { email = adminName }, _ct)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await user.PutAsJsonAsync("/api/account/profile", new { email = $"{name}@example.com" }, _ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await user.PutAsJsonAsync("/api/account/profile", new { email = $"{name}@example.com" }, _ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("http://localhost", HttpStatusCode.Unauthorized)]
    [InlineData("https://elsewhere.example", HttpStatusCode.Forbidden)]
    public async Task SignInAcceptsItsOwnOriginWithoutConfiguration(string origin, HttpStatusCode expected)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", origin);

        var response = await client.PostAsJsonAsync("/api/auth/login", new { username = "nobody@example.com", password = "wrong-password" }, _ct);

        response.StatusCode.Should().Be(expected);
    }

    [Fact]
    public async Task FailedSignInsLockOnlyThatAccount()
    {
        var admin = await factory.CreateAdminClientAsync();
        var locked = $"user{Guid.NewGuid():N}"[..16];
        var other = $"user{Guid.NewGuid():N}"[..16];
        foreach (var name in new[] { locked, other })
            (await admin.PostAsJsonAsync("/api/admin/users", new { username = name, password = "Secret-Pass-123", role = "User" }, _ct)).EnsureSuccessStatusCode();

        for (var i = 0; i < 5; i++)
            (await LoginAsync(locked, "wrong-password")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        (await LoginAsync(locked, "Secret-Pass-123")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await LoginAsync(other, "Secret-Pass-123")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SpoofedForwardedAddressesDoNotBypassTheLockout()
    {
        var name = $"user{Guid.NewGuid():N}"[..16];
        for (var i = 0; i < 5; i++)
        {
            var client = factory.CreateClient();
            client.DefaultRequestHeaders.Add("X-Forwarded-For", $"203.0.113.{i + 1}");
            (await client.PostAsJsonAsync("/api/auth/login", new { username = name, password = "wrong-password" }, _ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        var last = factory.CreateClient();
        last.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.99");
        (await last.PostAsJsonAsync("/api/auth/login", new { username = name, password = "wrong-password" }, _ct)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }
}
