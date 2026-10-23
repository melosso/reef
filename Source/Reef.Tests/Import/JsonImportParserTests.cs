using System.Text;
using FluentAssertions;
using Reef.Core.Models;
using Reef.Core.Parsers;

namespace Reef.Tests.Import;

public class JsonImportParserTests
{
    private readonly JsonImportParser _parser = new();

    private static Stream ToStream(string text) =>
        new MemoryStream(Encoding.UTF8.GetBytes(text));

    private static ImportFormatConfig DefaultConfig() => new();

    private static async Task<List<ParsedRow>> CollectAsync(IAsyncEnumerable<ParsedRow> rows)
    {
        var result = new List<ParsedRow>();
        await foreach (var row in rows)
            result.Add(row);
        return result;
    }

    [Fact]
    public async Task ParseAsyncSimpleJsonArrayReturnsCorrectRows()
    {
        var json = """[{"name":"Alice","age":30},{"name":"Bob","age":25}]""";
        var rows = await CollectAsync(_parser.ParseAsync(ToStream(json), DefaultConfig(), TestContext.Current.CancellationToken));

        rows.Should().HaveCount(2);
        rows[0].Columns["name"].Should().Be("Alice");
        rows[0].Columns["age"].Should().Be(30L);
        rows[1].Columns["name"].Should().Be("Bob");
    }

    [Fact]
    public async Task ParseAsyncJsonWithDataRootPathExtractsNestedArray()
    {
        var json = """{"meta":{"total":2},"data":[{"id":1,"name":"Alice"},{"id":2,"name":"Bob"}]}""";
        var config = DefaultConfig();
        config.DataRootPath = "$.data";

        var rows = await CollectAsync(_parser.ParseAsync(ToStream(json), config, TestContext.Current.CancellationToken));

        rows.Should().HaveCount(2);
        rows[0].Columns["id"].Should().Be(1L);
        rows[0].Columns["name"].Should().Be("Alice");
        rows[1].Columns["name"].Should().Be("Bob");
    }

    [Fact]
    public async Task ParseAsyncSingleJsonObjectReturnsOneRow()
    {
        var json = """{"name":"Alice","age":30}""";
        var rows = await CollectAsync(_parser.ParseAsync(ToStream(json), DefaultConfig(), TestContext.Current.CancellationToken));

        rows.Should().HaveCount(1);
        rows[0].Columns["name"].Should().Be("Alice");
    }

    [Fact]
    public async Task ParseAsyncJsonWithNullValuesReturnsNullForNullFields()
    {
        var json = """[{"name":"Alice","email":null}]""";
        var rows = await CollectAsync(_parser.ParseAsync(ToStream(json), DefaultConfig(), TestContext.Current.CancellationToken));

        rows.Should().HaveCount(1);
        rows[0].Columns["email"].Should().BeNull();
    }

    [Fact]
    public async Task ParseAsyncJsonWithBoolValuesParsesCorrectly()
    {
        var json = """[{"name":"Alice","active":true,"deleted":false}]""";
        var rows = await CollectAsync(_parser.ParseAsync(ToStream(json), DefaultConfig(), TestContext.Current.CancellationToken));

        rows.Should().HaveCount(1);
        rows[0].Columns["active"].Should().Be(true);
        rows[0].Columns["deleted"].Should().Be(false);
    }

    [Fact]
    public async Task ParseAsyncJsonLinesParsesEachLineAsRow()
    {
        var jsonl = """
            {"id":1,"name":"Alice"}
            {"id":2,"name":"Bob"}
            {"id":3,"name":"Carol"}
            """;
        var config = DefaultConfig();
        config.IsJsonLines = true;

        var rows = await CollectAsync(_parser.ParseAsync(ToStream(jsonl), config, TestContext.Current.CancellationToken));

        rows.Should().HaveCount(3);
        rows[0].Columns["id"].Should().Be(1L);
        rows[2].Columns["name"].Should().Be("Carol");
    }

    [Fact]
    public async Task ParseAsyncJsonLinesSkipsBlankLines()
    {
        var jsonl = "{\"id\":1}\n\n{\"id\":2}\n";
        var config = DefaultConfig();
        config.IsJsonLines = true;

        var rows = await CollectAsync(_parser.ParseAsync(ToStream(jsonl), config, TestContext.Current.CancellationToken));

        rows.Should().HaveCount(2);
    }

    [Fact]
    public async Task ParseAsyncInvalidJsonReturnsParseErrorRow()
    {
        var json = "this is not json";
        var rows = await CollectAsync(_parser.ParseAsync(ToStream(json), DefaultConfig(), TestContext.Current.CancellationToken));

        rows.Should().HaveCount(1);
        rows[0].ParseError.Should().NotBeNull();
        rows[0].ParseError.Should().Contain("JSON parse error");
    }

    [Fact]
    public async Task ParseAsyncInvalidJsonLineReturnsParseErrorRowForThatLine()
    {
        var jsonl = "{\"id\":1}\nbad line\n{\"id\":3}\n";
        var config = DefaultConfig();
        config.IsJsonLines = true;

        var rows = await CollectAsync(_parser.ParseAsync(ToStream(jsonl), config, TestContext.Current.CancellationToken));

        rows.Should().HaveCount(3);
        rows[1].ParseError.Should().NotBeNull();
        rows[0].ParseError.Should().BeNull();
        rows[2].ParseError.Should().BeNull();
    }

    [Fact]
    public async Task ParseAsyncDataRootPathWithDotPrefixNavigatesCorrectly()
    {
        var json = """{"results":{"items":[{"x":1},{"x":2}]}}""";
        var config = DefaultConfig();
        config.DataRootPath = "$.results.items";

        var rows = await CollectAsync(_parser.ParseAsync(ToStream(json), config, TestContext.Current.CancellationToken));

        rows.Should().HaveCount(2);
        rows[0].Columns["x"].Should().Be(1L);
    }

    [Fact]
    public async Task ParseAsyncEmptyJsonArrayReturnsNoRows()
    {
        var rows = await CollectAsync(_parser.ParseAsync(ToStream("[]"), DefaultConfig(), TestContext.Current.CancellationToken));
        rows.Should().BeEmpty();
    }

    [Fact]
    public async Task ParseAsyncLineNumbersAssignedAreSequential()
    {
        var json = """[{"a":1},{"a":2},{"a":3}]""";
        var rows = await CollectAsync(_parser.ParseAsync(ToStream(json), DefaultConfig(), TestContext.Current.CancellationToken));

        rows[0].LineNumber.Should().Be(1);
        rows[1].LineNumber.Should().Be(2);
        rows[2].LineNumber.Should().Be(3);
    }

    [Fact]
    public async Task ParseAsyncDecimalNumberParsesAsDouble()
    {
        var json = """[{"price":19.99}]""";
        var rows = await CollectAsync(_parser.ParseAsync(ToStream(json), DefaultConfig(), TestContext.Current.CancellationToken));

        rows.Should().HaveCount(1);
        rows[0].Columns["price"].Should().Be(19.99);
    }
}
