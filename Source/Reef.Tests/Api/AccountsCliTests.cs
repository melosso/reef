using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Reef.Helpers;
using Xunit;

namespace Reef.Tests.Api;

[Collection(ApiCollection.Name)]
public class AccountsCliTests(ReefApiFactory factory)
{
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private Task<int> RunAsync(params string[] args) => RunAsync(Path.Combine(factory.DataFolder, "Reef.db"), args);

    private static Task<int> RunAsync(string databasePath, string[] args) =>
        AccountsCli.RunAsync(["accounts", .. args], new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Reef:DatabasePath"] = databasePath })
            .Build());

    private async Task<HttpStatusCode> LoginAsync(string username, string password) =>
        (await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { username, password }, _ct)).StatusCode;

    [Fact]
    public async Task Created_account_signs_in_with_the_password_set_from_the_shell()
    {
        factory.CreateClient();
        var name = $"cli{Guid.NewGuid():N}"[..16];

        (await RunAsync("create", name, "First-Pass-123", "user")).Should().Be(0);
        (await LoginAsync(name, "First-Pass-123")).Should().Be(HttpStatusCode.OK);

        (await RunAsync("password", name, "Second-Pass-123")).Should().Be(0);
        (await LoginAsync(name, "First-Pass-123")).Should().Be(HttpStatusCode.Unauthorized);
        (await LoginAsync(name, "Second-Pass-123")).Should().Be(HttpStatusCode.OK);

        (await RunAsync("disable", name)).Should().Be(0);
        (await LoginAsync(name, "Second-Pass-123")).Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Rejects_short_passwords_unknown_accounts_and_removing_the_last_admin()
    {
        var (admin, _) = await factory.SeededAdminAsync();

        (await RunAsync("create", "shortpass", "abc")).Should().Be(1);
        (await RunAsync("password", "nobody-here", "Secret-Pass-123")).Should().Be(1);
        (await RunAsync("demote", admin)).Should().Be(1);
        (await RunAsync("delete", admin)).Should().Be(1);
        (await RunAsync("unknown-command")).Should().Be(1);
    }

    [Fact]
    public async Task Refuses_to_create_a_database_that_does_not_exist()
    {
        var path = Path.Combine(factory.DataFolder, "missing.db");

        (await RunAsync(path, ["list"])).Should().Be(1);
        File.Exists(path).Should().BeFalse();
    }
}
