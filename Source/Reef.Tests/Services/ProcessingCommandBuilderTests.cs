using Dapper;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Reef.Core.Models;
using Reef.Core.Services;

namespace Reef.Tests.Services;

public class ProcessingCommandBuilderTests
{
    private const string Hostile = "O'Brien'); DROP TABLE Log;--";

    [Theory]
    [InlineData("INSERT INTO Log (Value, Rows) VALUES ('{splitkey}', {rowcount})")]
    [InlineData("INSERT INTO Log (Value, Rows) VALUES (@splitkey, {rowcount})")]
    public async Task SplitKeyIsBoundAsParameter(string command)
    {
        var config = new ProcessingConfig { Type = "Query", Command = command };
        var context = new ProcessingContext { SplitKey = Hostile, RowCount = 3, OutputFormat = "JSON", Status = "Success" };

        var (sql, parameters) = ProcessingCommandBuilder.Build("SQLite", config, context);

        await using var db = new SqliteConnection("Data Source=:memory:");
        await db.OpenAsync(TestContext.Current.CancellationToken);
        await db.ExecuteAsync("CREATE TABLE Log (Value TEXT, Rows INTEGER)");
        await db.ExecuteAsync(sql, new DynamicParameters(parameters.ToDictionary(p => p.Key, p => (object)p.Value)));
        var row = await db.QuerySingleAsync<(string Value, long Rows)>("SELECT Value, Rows FROM Log");
        row.Value.Should().Be(Hostile);
        row.Rows.Should().Be(3);
    }
}
