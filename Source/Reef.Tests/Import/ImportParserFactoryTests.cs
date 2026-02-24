using FluentAssertions;
using Reef.Core.Parsers;

namespace Reef.Tests.Import;

public class ImportParserFactoryTests
{
    [Theory]
    [InlineData("CSV")]
    [InlineData("csv")]
    [InlineData("Csv")]
    public void Create_CsvFormat_ReturnsCsvParser(string format)
    {
        var parser = ImportParserFactory.Create(format);
        parser.Should().BeOfType<CsvImportParser>();
    }

    [Theory]
    [InlineData("TSV")]
    [InlineData("tsv")]
    public void Create_TsvFormat_ReturnsCsvParser(string format)
    {
        // TSV uses CSV parser with tab delimiter
        var parser = ImportParserFactory.Create(format);
        parser.Should().BeOfType<CsvImportParser>();
    }

    [Theory]
    [InlineData("JSON")]
    [InlineData("json")]
    [InlineData("JSONL")]
    [InlineData("jsonl")]
    public void Create_JsonFormat_ReturnsJsonParser(string format)
    {
        var parser = ImportParserFactory.Create(format);
        parser.Should().BeOfType<JsonImportParser>();
    }

    [Theory]
    [InlineData("XML")]
    [InlineData("xml")]
    public void Create_XmlFormat_ReturnsXmlParser(string format)
    {
        var parser = ImportParserFactory.Create(format);
        parser.Should().BeOfType<XmlImportParser>();
    }

    [Theory]
    [InlineData("YAML")]
    [InlineData("yaml")]
    [InlineData("YML")]
    [InlineData("yml")]
    public void Create_YamlFormat_ReturnsYamlParser(string format)
    {
        var parser = ImportParserFactory.Create(format);
        parser.Should().BeOfType<YamlImportParser>();
    }

    [Theory]
    [InlineData("EXCEL")]
    [InlineData("PARQUET")]
    [InlineData("UNKNOWN")]
    public void Create_UnsupportedFormat_ThrowsNotSupportedException(string format)
    {
        var act = () => ImportParserFactory.Create(format);
        act.Should().Throw<NotSupportedException>()
            .WithMessage($"*'{format}'*");
    }

    [Fact]
    public void SupportedFormats_ContainsAllExpectedFormats()
    {
        var formats = ImportParserFactory.SupportedFormats;
        formats.Should().Contain(["CSV", "TSV", "JSON", "JSONL", "XML", "YAML"]);
    }
}
