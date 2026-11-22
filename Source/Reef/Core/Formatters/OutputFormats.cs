namespace Reef.Core.Formatters;

public static class OutputFormats
{
    public static IFormatter Formatter(string format) => format.ToUpperInvariant() switch
    {
        "XML" => new XmlFormatter(),
        "CSV" => new CsvFormatter(),
        "YAML" => new YamlFormatter(),
        _ => new JsonFormatter()
    };

    public static string Extension(string format) => format.ToLowerInvariant() switch
    {
        "json" or "xml" or "csv" or "yaml" or "pdf" or "docx" or "odt" => format.ToLowerInvariant(),
        _ => "txt"
    };

    public static string MimeType(string extension) => extension switch
    {
        "json" => "application/json",
        "xml" => "application/xml",
        "csv" => "text/csv",
        "yaml" => "application/yaml",
        "pdf" => "application/pdf",
        "docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        "odt" => "application/vnd.oasis.opendocument.text",
        _ => "text/plain"
    };
}
