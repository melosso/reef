namespace Reef.Core.Parsers;

/// <summary>
/// Factory that selects the correct IImportParser implementation based on format string.
/// </summary>
public static class ImportParserFactory
{
    public static IImportParser Create(string format) => format.ToUpperInvariant() switch
    {
        "CSV"  => new CsvImportParser(),
        "TSV"  => new CsvImportParser(),  // TSV uses the CSV parser with tab delimiter (FormatConfig.Delimiter = "\t")
        "JSON" => new JsonImportParser(),
        "JSONL" => new JsonImportParser(), // JSONL handled via FormatConfig.IsJsonLines = true
        "XML"  => new XmlImportParser(),
        "YAML" or "YML" => new YamlImportParser(),
        _ => throw new NotSupportedException($"Import format '{format}' is not supported. Supported: CSV, TSV, JSON, JSONL, XML, YAML")
    };

    public static string[] SupportedFormats => new[]
    {
        "CSV", "TSV", "JSON", "JSONL", "XML", "YAML"
    };
}
