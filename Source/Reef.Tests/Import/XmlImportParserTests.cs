using System.Text;
using FluentAssertions;
using Reef.Core.Models;
using Reef.Core.Parsers;

namespace Reef.Tests.Import;

public class XmlImportParserTests
{
    private readonly XmlImportParser _parser = new();

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
    public async Task ParseAsync_SimpleXml_ExtractsChildElements()
    {
        var xml = """
            <Orders>
                <Order><Id>1</Id><Name>Alice</Name></Order>
                <Order><Id>2</Id><Name>Bob</Name></Order>
            </Orders>
            """;

        var rows = await CollectAsync(_parser.ParseAsync(ToStream(xml), DefaultConfig()));

        rows.Should().HaveCount(2);
        rows[0].Columns["Id"].Should().Be("1");
        rows[0].Columns["Name"].Should().Be("Alice");
        rows[1].Columns["Id"].Should().Be("2");
    }

    [Fact]
    public async Task ParseAsync_WithRecordElementXPath_UsesCustomXPath()
    {
        var xml = """
            <Root>
                <Items>
                    <Item><Code>A</Code></Item>
                    <Item><Code>B</Code></Item>
                </Items>
            </Root>
            """;

        var config = DefaultConfig();
        config.RecordElement = "//Item";

        var rows = await CollectAsync(_parser.ParseAsync(ToStream(xml), config));

        rows.Should().HaveCount(2);
        rows[0].Columns["Code"].Should().Be("A");
        rows[1].Columns["Code"].Should().Be("B");
    }

    [Fact]
    public async Task ParseAsync_XmlWithAttributes_ExtractsAttributesWithAtPrefix()
    {
        var xml = """
            <Orders>
                <Order id="1" status="active"><Name>Alice</Name></Order>
            </Orders>
            """;

        var rows = await CollectAsync(_parser.ParseAsync(ToStream(xml), DefaultConfig()));

        rows.Should().HaveCount(1);
        rows[0].Columns["@id"].Should().Be("1");
        rows[0].Columns["@status"].Should().Be("active");
        rows[0].Columns["Name"].Should().Be("Alice");
    }

    [Fact]
    public async Task ParseAsync_InvalidXml_ReturnsParseErrorRow()
    {
        var xml = "<Orders><Order><Id>1</Id></Orders>"; // unclosed Order tag

        var rows = await CollectAsync(_parser.ParseAsync(ToStream(xml), DefaultConfig()));

        rows.Should().HaveCount(1);
        rows[0].ParseError.Should().NotBeNull();
        rows[0].ParseError.Should().Contain("XML parse error");
    }

    [Fact]
    public async Task ParseAsync_InvalidXPath_ReturnsParseErrorRow()
    {
        var xml = "<Orders><Order><Id>1</Id></Order></Orders>";
        var config = DefaultConfig();
        config.RecordElement = "//[invalid";

        var rows = await CollectAsync(_parser.ParseAsync(ToStream(xml), config));

        rows.Should().HaveCount(1);
        rows[0].ParseError.Should().NotBeNull();
        rows[0].ParseError.Should().Contain("XPath error");
    }

    [Fact]
    public async Task ParseAsync_NoMatchingElements_ReturnsNoRows()
    {
        var xml = "<Orders><Order><Id>1</Id></Order></Orders>";
        var config = DefaultConfig();
        config.RecordElement = "//NonExistent";

        var rows = await CollectAsync(_parser.ParseAsync(ToStream(xml), config));

        rows.Should().BeEmpty();
    }

    [Fact]
    public async Task ParseAsync_LineNumbersAssigned_AreSequential()
    {
        var xml = """
            <Items>
                <Item><V>1</V></Item>
                <Item><V>2</V></Item>
                <Item><V>3</V></Item>
            </Items>
            """;

        var rows = await CollectAsync(_parser.ParseAsync(ToStream(xml), DefaultConfig()));

        rows[0].LineNumber.Should().Be(1);
        rows[1].LineNumber.Should().Be(2);
        rows[2].LineNumber.Should().Be(3);
    }
}
