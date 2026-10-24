using Dapper;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Reef.Core.Database;

namespace Reef.Tests.Database;

public class UlidMigrationTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("reef-ulid").FullName;

    private string ConnectionString => $"Data Source={Path.Combine(_folder, "reef.db")};Pooling=False";

    private async Task<SqliteConnection> OpenAsync()
    {
        var db = new SqliteConnection(ConnectionString);
        await db.OpenAsync();
        return db;
    }

    [Fact]
    public async Task ExistingRowsKeepTheirIdentityAndGetAStableUlid()
    {
        await using (var db = await OpenAsync())
            await db.ExecuteAsync("CREATE TABLE Things (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT); INSERT INTO Things (Name) VALUES ('a'), ('b');");

        await new UlidMigration(ConnectionString).ApplyAsync();
        await using var check = await OpenAsync();
        var first = (await check.QueryAsync<(long Id, string Ulid)>("SELECT Id, Ulid FROM Things ORDER BY Id")).ToList();

        await new UlidMigration(ConnectionString).ApplyAsync();
        var second = (await check.QueryAsync<(long Id, string Ulid)>("SELECT Id, Ulid FROM Things ORDER BY Id")).ToList();

        first.Select(r => r.Id).Should().Equal(1, 2);
        first.Should().OnlyContain(r => UlidPattern.IsMatch(r.Ulid));
        first.Select(r => r.Ulid).Should().OnlyHaveUniqueItems();
        second.Should().Equal(first);
    }

    [Fact]
    public async Task NewRowsReceiveAUlidWithoutTheInsertNamingIt()
    {
        await using var db = await OpenAsync();
        await db.ExecuteAsync("CREATE TABLE Things (Id INTEGER PRIMARY KEY AUTOINCREMENT, Name TEXT)");
        await new UlidMigration(ConnectionString).ApplyAsync();

        var id = await db.ExecuteScalarAsync<long>("INSERT INTO Things (Name) VALUES ('c'); SELECT last_insert_rowid();");
        var ulids = (await db.QueryAsync<string>("INSERT INTO Things (Name) VALUES ('d'); SELECT Ulid FROM Things ORDER BY Id")).ToList();

        id.Should().Be(1);
        ulids.Should().HaveCount(2).And.OnlyContain(u => UlidPattern.IsMatch(u)).And.OnlyHaveUniqueItems();
        var duplicate = () => db.ExecuteAsync("UPDATE Things SET Ulid = (SELECT Ulid FROM Things WHERE Id = 1) WHERE Id = 2");
        await duplicate.Should().ThrowAsync<SqliteException>();
    }

    private static readonly System.Text.RegularExpressions.Regex UlidPattern = new("^[0-9A-HJKMNP-TV-Z]{26}$");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_folder, recursive: true);
    }
}
