using System.Text;
using FluentAssertions;
using Reef.Core.Models;
using Reef.Core.Parsers;

namespace Reef.Tests.Import;

public class CsvImportParserTests
{
    private readonly CsvImportParser _parser = new();

    private static Stream ToStream(string text) =>
        new MemoryStream(Encoding.UTF8.GetBytes(text));

    private static ImportFormatConfig DefaultConfig() => new()
    {
        Delimiter = ",",
        HasHeader = true,
        TrimWhitespace = true,
        QuoteChar = "\"",
        Encoding = "UTF-8"
    };

    private static async Task<List<ParsedRow>> CollectAsync(IAsyncEnumerable<ParsedRow> rows)
    {
        var result = new List<ParsedRow>();
        await foreach (var row in rows)
            result.Add(row);
        return result;
    }

    [Fact]
    public async Task ParseAsync_SimpleHeaderedCsv_ReturnsCorrectRows()
    {
        var csv = "Name,Age,City\nAlice,30,London\nBob,25,Paris\n";
        var rows = await CollectAsync(_parser.ParseAsync(ToStream(csv), DefaultConfig()));

        rows.Should().HaveCount(2);
        rows[0].Columns["Name"].Should().Be("Alice");
        rows[0].Columns["Age"].Should().Be("30");
        rows[0].Columns["City"].Should().Be("London");
        rows[1].Columns["Name"].Should().Be("Bob");
        rows[1].ParseError.Should().BeNull();
    }

    [Fact]
    public async Task ParseAsync_NoHeader_GeneratesColNames()
    {
        var csv = "Alice,30\nBob,25\n";
        var config = DefaultConfig();
        config.HasHeader = false;

        var rows = await CollectAsync(_parser.ParseAsync(ToStream(csv), config));

        rows.Should().HaveCount(2);
        rows[0].Columns.Should().ContainKey("Col1");
        rows[0].Columns.Should().ContainKey("Col2");
        rows[0].Columns["Col1"].Should().Be("Alice");
        rows[0].Columns["Col2"].Should().Be("30");
    }

    [Fact]
    public async Task ParseAsync_QuotedFieldsWithComma_ParsesCorrectly()
    {
        var csv = "Name,Address\n\"Smith, John\",\"123 Main St, NY\"\n";
        var rows = await CollectAsync(_parser.ParseAsync(ToStream(csv), DefaultConfig()));

        rows.Should().HaveCount(1);
        rows[0].Columns["Name"].Should().Be("Smith, John");
        rows[0].Columns["Address"].Should().Be("123 Main St, NY");
    }

    [Fact]
    public async Task ParseAsync_EscapedQuotesInField_ParsesCorrectly()
    {
        var csv = "Name,Quote\nAlice,\"He said \"\"hello\"\"\"\n";
        var rows = await CollectAsync(_parser.ParseAsync(ToStream(csv), DefaultConfig()));

        rows.Should().HaveCount(1);
        rows[0].Columns["Quote"].Should().Be("He said \"hello\"");
    }

    [Fact]
    public async Task ParseAsync_TsvWithTabDelimiter_ParsesCorrectly()
    {
        var tsv = "Name\tAge\tCity\nAlice\t30\tLondon\n";
        var config = DefaultConfig();
        config.Delimiter = "\t";

        var rows = await CollectAsync(_parser.ParseAsync(ToStream(tsv), config));

        rows.Should().HaveCount(1);
        rows[0].Columns["Name"].Should().Be("Alice");
        rows[0].Columns["City"].Should().Be("London");
    }

    [Fact]
    public async Task ParseAsync_NullValue_ReturnsNullForMatchingFields()
    {
        var csv = "Name,Age\nAlice,\\N\nBob,25\n";
        var config = DefaultConfig();
        config.NullValue = "\\N";

        var rows = await CollectAsync(_parser.ParseAsync(ToStream(csv), config));

        rows.Should().HaveCount(2);
        rows[0].Columns["Age"].Should().BeNull();
        rows[1].Columns["Age"].Should().Be("25");
    }

    [Fact]
    public async Task ParseAsync_SkipRows_SkipsSpecifiedNumberOfRows()
    {
        var csv = "# Comment line\nName,Age\nAlice,30\n";
        var config = DefaultConfig();
        config.SkipRows = 1;

        var rows = await CollectAsync(_parser.ParseAsync(ToStream(csv), config));

        rows.Should().HaveCount(1);
        rows[0].Columns["Name"].Should().Be("Alice");
    }

    [Fact]
    public async Task ParseAsync_EmptyLines_SkipsEmptyLines()
    {
        var csv = "Name,Age\nAlice,30\n\nBob,25\n";
        var rows = await CollectAsync(_parser.ParseAsync(ToStream(csv), DefaultConfig()));

        rows.Should().HaveCount(2);
    }

    [Fact]
    public async Task ParseAsync_TrimWhitespace_TrimsFieldValues()
    {
        var csv = "Name , Age\n Alice , 30 \n";
        var rows = await CollectAsync(_parser.ParseAsync(ToStream(csv), DefaultConfig()));

        rows.Should().HaveCount(1);
        rows[0].Columns["Name"].Should().Be("Alice");
        rows[0].Columns["Age"].Should().Be("30");
    }

    [Fact]
    public async Task ParseAsync_LineNumbers_AreAssignedCorrectly()
    {
        var csv = "Name,Age\nAlice,30\nBob,25\n";
        var rows = await CollectAsync(_parser.ParseAsync(ToStream(csv), DefaultConfig()));

        rows[0].LineNumber.Should().Be(2); // header is line 1
        rows[1].LineNumber.Should().Be(3);
    }

    [Fact]
    public async Task ParseAsync_EmptyStream_ReturnsNoRows()
    {
        var rows = await CollectAsync(_parser.ParseAsync(ToStream(""), DefaultConfig()));
        rows.Should().BeEmpty();
    }

    [Fact]
    public async Task ParseAsync_HeaderOnlyNoCsvRows_ReturnsNoRows()
    {
        var rows = await CollectAsync(_parser.ParseAsync(ToStream("Name,Age\n"), DefaultConfig()));
        rows.Should().BeEmpty();
    }

    [Fact]
    public async Task ParseAsync_FewerColumnsThanHeader_FillsWithEmpty()
    {
        var csv = "Name,Age,City\nAlice,30\n";
        var rows = await CollectAsync(_parser.ParseAsync(ToStream(csv), DefaultConfig()));

        rows.Should().HaveCount(1);
        rows[0].Columns["City"].Should().Be(string.Empty);
    }

    [Fact]
    public async Task ParseAsync_CancellationRequested_ThrowsOperationCanceledException()
    {
        var csv = "Name,Age\nAlice,30\nBob,25\nCarol,35\n";
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await CollectAsync(_parser.ParseAsync(ToStream(csv), DefaultConfig(), cts.Token));

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
