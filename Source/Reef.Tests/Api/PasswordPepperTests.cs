using System.Net;
using System.Net.Http.Json;
using Dapper;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Reef.Tests.Api;

[Collection(ApiCollection.Name)]
public class PasswordPepperTests(ReefApiFactory factory)
{
    private const string Password = "Secret-Pass-123";
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private SqliteConnection Open() => new($"Data Source={Path.Combine(factory.DataFolder, "Reef.db")};Pooling=False");

    private async Task<string> CreateUserAsync()
    {
        var admin = await factory.CreateAdminClientAsync();
        var name = $"user{Guid.NewGuid():N}"[..16];
        (await admin.PostAsJsonAsync("/api/admin/users", new { username = name, password = Password, role = "User" }, _ct)).EnsureSuccessStatusCode();
        return name;
    }

    private async Task<string> StoredHashAsync(string username)
    {
        await using var conn = Open();
        return await conn.ExecuteScalarAsync<string>("SELECT PasswordHash FROM Users WHERE Username = @username", new { username }) ?? "";
    }

    private async Task<HttpStatusCode> LoginAsync(string username) =>
        (await factory.CreateClient().PostAsJsonAsync("/api/auth/login", new { username, password = Password }, _ct)).StatusCode;

    [Fact]
    public async Task Stored_password_hashes_depend_on_the_encryption_key()
    {
        var name = await CreateUserAsync();

        BCrypt.Net.BCrypt.Verify(Password, await StoredHashAsync(name)).Should().BeFalse();
        (await LoginAsync(name)).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Legacy_hashes_still_sign_in_and_are_upgraded()
    {
        var name = await CreateUserAsync();
        await using (var conn = Open())
            await conn.ExecuteAsync("UPDATE Users SET PasswordHash = @hash WHERE Username = @name",
                new { hash = BCrypt.Net.BCrypt.HashPassword(Password, workFactor: 4), name });

        (await LoginAsync(name)).Should().Be(HttpStatusCode.OK);

        BCrypt.Net.BCrypt.Verify(Password, await StoredHashAsync(name)).Should().BeFalse();
        (await LoginAsync(name)).Should().Be(HttpStatusCode.OK);
    }
}
