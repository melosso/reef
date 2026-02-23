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
    public async Task ParseAsync_SimpleJsonArray_ReturnsCorrectRows()
    {
        var json = """[{"name":"Alice","age":30},{"name":"Bob","age":25}]""";
        var rows = await CollectAsync(_parser.ParseAsync(ToStream(json), DefaultConfig()));

        rows.Should().HaveCount(2);
        rows[0].Columns["name"].Should().Be("Alice");
        rows[0].Columns["age"].Should().Be(30L);
        rows[1].Columns["name"].Should().Be("Bob");
    }

    [Fact]
    public async Task ParseAsync_JsonWithDataRootPath_ExtractsNestedArray()
    {
        var json = """{"meta":{"total":2},"data":[{"id":1,"name":"Alice"},{"id":2,"name":"Bob"}]}""";
        var config = DefaultConfig();
        config.DataRootPath = "$.data";

        var rows = await CollectAsync(_parser.ParseAsync(ToStream(json), config));

        rows.Should().HaveCount(2);
        rows[0].Columns["id"].Should().Be(1L);
        rows[0].Columns["name"].Should().Be("Alice");
        rows[1].Columns["name"].Should().Be("Bob");
    }

    [Fact]
    public async Task ParseAsync_SingleJsonObject_ReturnsOneRow()
    {
        var json = """{"name":"Alice","age":30}""";
        var rows = await CollectAsync(_parser.ParseAsync(ToStream(json), DefaultConfig()));

        rows.Should().HaveCount(1);
        rows[0].Columns["name"].Should().Be("Alice");
    }

    [Fact]
    public async Task ParseAsync_JsonWithNullValues_ReturnsNullForNullFields()
    {
        var json = """[{"name":"Alice","email":null}]""";
        var rows = await CollectAsync(_parser.ParseAsync(ToStream(json), DefaultConfig()));

        rows.Should().HaveCount(1);
        rows[0].Columns["email"].Should().BeNull();
    }

    [Fact]
    public async Task ParseAsync_JsonWithBoolValues_ParsesCorrectly()
    {
        var json = """[{"name":"Alice","active":true,"deleted":false}]""";
        var rows = await CollectAsync(_parser.ParseAsync(ToStream(json), DefaultConfig()));

        rows.Should().HaveCount(1);
        rows[0].Columns["active"].Should().Be(true);
        rows[0].Columns["deleted"].Should().Be(false);
    }

    [Fact]
    public async Task ParseAsync_JsonLines_ParsesEachLineAsRow()
    {
        var jsonl = """
            {"id":1,"name":"Alice"}
            {"id":2,"name":"Bob"}
            {"id":3,"name":"Carol"}
            """;
        var config = DefaultConfig();
        config.IsJsonLines = true;

        var rows = await CollectAsync(_parser.ParseAsync(ToStream(jsonl), config));

        rows.Should().HaveCount(3);
        rows[0].Columns["id"].Should().Be(1L);
        rows[2].Columns["name"].Should().Be("Carol");
    }

    [Fact]
    public async Task ParseAsync_JsonLines_SkipsBlankLines()
    {
        var jsonl = "{\"id\":1}\n\n{\"id\":2}\n";
        var config = DefaultConfig();
        config.IsJsonLines = true;

        var rows = await CollectAsync(_parser.ParseAsync(ToStream(jsonl), config));

        rows.Should().HaveCount(2);
    }

    [Fact]
    public async Task ParseAsync_InvalidJson_ReturnsParseErrorRow()
    {
        var json = "this is not json";
        var rows = await CollectAsync(_parser.ParseAsync(ToStream(json), DefaultConfig()));

        rows.Should().HaveCount(1);
        rows[0].ParseError.Should().NotBeNull();
        rows[0].ParseError.Should().Contain("JSON parse error");
    }

    [Fact]
    public async Task ParseAsync_InvalidJsonLine_ReturnsParseErrorRowForThatLine()
    {
        var jsonl = "{\"id\":1}\nbad line\n{\"id\":3}\n";
        var config = DefaultConfig();
        config.IsJsonLines = true;

        var rows = await CollectAsync(_parser.ParseAsync(ToStream(jsonl), config));

        rows.Should().HaveCount(3);
        rows[1].ParseError.Should().NotBeNull();
        rows[0].ParseError.Should().BeNull();
        rows[2].ParseError.Should().BeNull();
    }

    [Fact]
    public async Task ParseAsync_DataRootPathWithDotPrefix_NavigatesCorrectly()
    {
        var json = """{"results":{"items":[{"x":1},{"x":2}]}}""";
        var config = DefaultConfig();
        config.DataRootPath = "$.results.items";

        var rows = await CollectAsync(_parser.ParseAsync(ToStream(json), config));

        rows.Should().HaveCount(2);
        rows[0].Columns["x"].Should().Be(1L);
    }

    [Fact]
    public async Task ParseAsync_EmptyJsonArray_ReturnsNoRows()
    {
        var rows = await CollectAsync(_parser.ParseAsync(ToStream("[]"), DefaultConfig()));
        rows.Should().BeEmpty();
    }

    [Fact]
    public async Task ParseAsync_LineNumbersAssigned_AreSequential()
    {
        var json = """[{"a":1},{"a":2},{"a":3}]""";
        var rows = await CollectAsync(_parser.ParseAsync(ToStream(json), DefaultConfig()));

        rows[0].LineNumber.Should().Be(1);
        rows[1].LineNumber.Should().Be(2);
        rows[2].LineNumber.Should().Be(3);
    }

    [Fact]
    public async Task ParseAsync_DecimalNumber_ParsesAsDouble()
    {
        var json = """[{"price":19.99}]""";
        var rows = await CollectAsync(_parser.ParseAsync(ToStream(json), DefaultConfig()));

        rows.Should().HaveCount(1);
        rows[0].Columns["price"].Should().Be(19.99);
    }
}
