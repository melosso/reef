using System.Runtime.CompilerServices;
using FluentAssertions;
using Reef.Core.Models;
using Reef.Core.Services;

namespace Reef.Tests.Services;

public class QueryExecutorTests
{
    // plain connection strings skip decryption, and a real EncryptionService would pin the process wide key before the api tests set theirs
    private static QueryExecutor Executor() => new((EncryptionService)RuntimeHelpers.GetUninitializedObject(typeof(EncryptionService)), new ValidationService());

    private static readonly Connection Sqlite = new() { Name = "memory", Type = "SQLite", ConnectionString = "Data Source=:memory:", Hash = "" };

    [Fact]
    public async Task QueryBindsStringParameters()
    {
        var (success, rows, error, _) = await Executor().ExecuteQueryAsync(Sqlite, "SELECT @name AS Name", new() { ["@name"] = "O'Brien" });

        success.Should().BeTrue(error);
        rows.Single()["Name"].Should().Be("O'Brien");
    }

    [Fact]
    public async Task CommandBindsStringParameters()
    {
        var (success, _, error, _) = await Executor().ExecuteCommandAsync(Sqlite, "SELECT @name", new() { ["@name"] = "O'Brien" });

        success.Should().BeTrue(error);
    }
}
