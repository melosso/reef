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
    public async Task Sign_in_accepts_the_account_email_address()
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
    public async Task First_admin_gets_a_random_account_from_the_log_and_must_change_the_password()
    {
        var (username, changeRequired) = await factory.SeededAdminAsync();

        username.Should().MatchRegex(@"^admin-[a-z0-9]{8}@reef\.local$");
        changeRequired.Should().BeTrue();
        (await LoginAsync("admin@reef.local", "admin123")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
