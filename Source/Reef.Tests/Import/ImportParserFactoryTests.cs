using FluentAssertions;
using Reef.Core.Parsers;

namespace Reef.Tests.Import;

public class ImportParserFactoryTests
{
    [Theory]
    [InlineData("CSV")]
    [InlineData("csv")]
    [InlineData("Csv")]
    public void CreateCsvFormatReturnsCsvParser(string format)
    {
        var parser = ImportParserFactory.Create(format);
        parser.Should().BeOfType<CsvImportParser>();
    }

    [Theory]
    [InlineData("TSV")]
    [InlineData("tsv")]
    public void CreateTsvFormatReturnsCsvParser(string format)
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
    public void CreateJsonFormatReturnsJsonParser(string format)
    {
        var parser = ImportParserFactory.Create(format);
        parser.Should().BeOfType<JsonImportParser>();
    }

    [Theory]
    [InlineData("XML")]
    [InlineData("xml")]
    public void CreateXmlFormatReturnsXmlParser(string format)
    {
        var parser = ImportParserFactory.Create(format);
        parser.Should().BeOfType<XmlImportParser>();
    }

    [Theory]
    [InlineData("YAML")]
    [InlineData("yaml")]
    [InlineData("YML")]
    [InlineData("yml")]
    public void CreateYamlFormatReturnsYamlParser(string format)
    {
        var parser = ImportParserFactory.Create(format);
        parser.Should().BeOfType<YamlImportParser>();
    }

    [Theory]
    [InlineData("EXCEL")]
    [InlineData("PARQUET")]
    [InlineData("UNKNOWN")]
    public void CreateUnsupportedFormatThrowsNotSupportedException(string format)
    {
        var act = () => ImportParserFactory.Create(format);
        act.Should().Throw<NotSupportedException>()
            .WithMessage($"*'{format}'*");
    }

    [Fact]
    public void SupportedFormatsContainsAllExpectedFormats()
    {
        var formats = ImportParserFactory.SupportedFormats;
        formats.Should().Contain(["CSV", "TSV", "JSON", "JSONL", "XML", "YAML"]);
    }
}
