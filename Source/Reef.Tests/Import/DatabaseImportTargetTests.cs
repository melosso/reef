using System.Runtime.CompilerServices;
using Dapper;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Reef.Core.Models;
using Reef.Core.Services;
using Reef.Core.Targets;

namespace Reef.Tests.Import;

public class DatabaseImportTargetTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("reef-import").FullName;
    private readonly CancellationToken _ct = TestContext.Current.CancellationToken;

    private string ConnectionString => $"Data Source={Path.Combine(_folder, "target.db")};Pooling=False";

    // plain connection strings skip decryption, and a real EncryptionService would pin the process wide key before the api tests set theirs
    private static DatabaseImportTarget Target() => new((EncryptionService)RuntimeHelpers.GetUninitializedObject(typeof(EncryptionService)));

    private ImportWriteContext Context(string targetTable) => new()
    {
        TargetConnection = new Connection { Name = "target", Type = "SQLite", ConnectionString = ConnectionString, Hash = "" },
        TargetTable = targetTable,
        LoadStrategy = "Upsert",
        UpsertKeyColumns = ["Id"]
    };

    private async Task<long> UpsertTwiceAsync(string createSql, string targetTable, string countSql, string nameColumn = "Name")
    {
        await using (var db = new SqliteConnection(ConnectionString))
            await db.ExecuteAsync(createSql);

        var target = Target();
        var context = Context(targetTable);

        await target.WriteBatchAsync([new() { ["Id"] = 1, [nameColumn] = "first" }], context, _ct);
        await target.WriteBatchAsync([new() { ["Id"] = 1, [nameColumn] = "second" }], context, _ct);

        await using var check = new SqliteConnection(ConnectionString);
        return await check.ExecuteScalarAsync<long>(countSql);
    }

    [Fact]
    public async Task UpsertEscapesQuotesInTableAndColumnNames()
    {
        var rows = await UpsertTwiceAsync(
            "CREATE TABLE \"we\"\"ird\" (Id INTEGER PRIMARY KEY, \"full \"\"name\" TEXT)",
            "we\"ird",
            "SELECT COUNT(*) FROM \"we\"\"ird\" WHERE \"full \"\"name\" = 'second'",
            "full \"name");

        rows.Should().Be(1);
    }

    [Fact]
    public async Task UpsertQuotesSchemaQualifiedTablesPerPart()
    {
        var rows = await UpsertTwiceAsync(
            "CREATE TABLE Orders (Id INTEGER PRIMARY KEY, Name TEXT)",
            "main.Orders",
            "SELECT COUNT(*) FROM Orders WHERE Name = 'second'");

        rows.Should().Be(1);
    }

    [Theory]
    [InlineData("HardDelete", "SELECT COUNT(*) FROM \"Ord ers\"")]
    [InlineData("SoftDelete", "SELECT COUNT(*) FROM \"Ord ers\" WHERE \"Is Deleted\" = 0")]
    public async Task DeletesOnlyRowsThatLeftTheSource(string strategy, string remainingSql)
    {
        await using (var db = new SqliteConnection(ConnectionString))
            await db.ExecuteAsync("CREATE TABLE \"Ord ers\" (\"Reef Id\" TEXT PRIMARY KEY, \"Is Deleted\" INTEGER NOT NULL DEFAULT 0); INSERT INTO \"Ord ers\" (\"Reef Id\") VALUES ('a'), ('b''c'), ('d');");

        var profile = new ImportProfile { Name = "orders", TargetTable = "Ord ers", Hash = "", DeltaSyncDeleteStrategy = strategy, DeltaSyncDeleteColumn = "Is Deleted", DeltaSyncDeleteValue = "1" };
        var affected = await Target().ApplyDeletesAsync(["b'c", "d"], "Reef Id", profile, Context("Ord ers"), _ct);

        affected.Should().Be(2);
        await using var check = new SqliteConnection(ConnectionString);
        (await check.ExecuteScalarAsync<long>(remainingSql)).Should().Be(1);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_folder, recursive: true);
    }
}
