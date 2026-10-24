using Dapper;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Reef.Api;
using Reef.Core.Data;
using Reef.Core.Database;
using Reef.Core.Models;
using Reef.Core.Security;
using Xunit;

namespace Reef.Tests.Security;

public class OidcAccountLinkingTests : IAsyncLifetime
{
    private readonly string _folder = Directory.CreateTempSubdirectory("reef-oidc").FullName;
    private readonly UserPasswordHasher _hasher = new("OidcAccountLinkingTestsKey0123456789");
    private string ConnectionString => $"Data Source={Path.Combine(_folder, "Reef.db")};Pooling=False";

    public async ValueTask InitializeAsync()
    {
        await new DatabaseInitializer(ConnectionString).InitializeAsync();
        await new OidcMigration(ConnectionString).ApplyAsync();
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.ExecuteAsync(
            "INSERT INTO Users (Username, PasswordHash, Role, IsActive, Email, CreatedAt, OidcSubject) VALUES ('alice', 'x', 'Admin', 1, 'alice@corp.example', @now, '')",
            new { now = DateTime.UtcNow });
    }

    public ValueTask DisposeAsync()
    {
        Directory.Delete(_folder, recursive: true);
        return ValueTask.CompletedTask;
    }

    private async Task<User?> ResolveAsync(OidcIdentity identity)
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        var (user, _) = await OidcEndpoints.ResolveAccountAsync(connection, _hasher, new OidcSettings { CreateAccounts = false }, identity);
        return user;
    }

    [Fact]
    public async Task MatchingUsernameClaimDoesNotLinkAnAccount() =>
        (await ResolveAsync(new OidcIdentity("subject-1", "alice", "", false))).Should().BeNull();

    [Fact]
    public async Task UnverifiedEmailDoesNotLinkAnAccount() =>
        (await ResolveAsync(new OidcIdentity("subject-2", "someone", "alice@corp.example", false))).Should().BeNull();

    [Fact]
    public async Task VerifiedEmailLinksTheAccount() =>
        (await ResolveAsync(new OidcIdentity("subject-3", "someone", "alice@corp.example", true)))!.Username.Should().Be("alice");
}
