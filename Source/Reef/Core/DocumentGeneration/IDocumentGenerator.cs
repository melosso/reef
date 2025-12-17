namespace Reef.Core.DocumentGeneration;

/// <summary>
/// Interface for document generators (PDF, DOCX, ODT)
/// </summary>
public interface IDocumentGenerator
{
    /// <summary>
    /// Output format produced by this generator (PDF, DOCX, ODT)
    /// </summary>
    string OutputFormat { get; }

    /// <summary>
    /// Generate a document from data and layout definition
    /// </summary>
    /// <param name="data">Query results as list of dictionaries</param>
    /// <param name="layoutDefinition">Parsed document layout (sections, page setup)</param>
    /// <param name="outputPath">Full path where document should be saved</param>
    /// <param name="options">Optional document generation options</param>
    /// <returns>Success status, file size, and error message if failed</returns>
    Task<(bool Success, long FileSizeBytes, string? ErrorMessage)> GenerateAsync(
        List<Dictionary<string, object>> data,
        DocumentLayout layoutDefinition,
        string outputPath,
        DocumentOptions? options = null);
}

/// <summary>
/// Document layout definition parsed from template
/// </summary>
public class DocumentLayout
{
    public PageSetup PageSetup { get; set; } = new();
    public List<DocumentSection> Sections { get; set; } = new();
    public string OutputFormat { get; set; } = "PDF";
}

/// <summary>
/// Page setup configuration
/// </summary>
public class PageSetup
{
    public string Size { get; set; } = "A4"; // A4, Letter, Legal
    public string Orientation { get; set; } = "Portrait"; // Portrait, Landscape
    public Margins Margins { get; set; } = new();
}

/// <summary>
/// Page margins in millimeters
/// </summary>
public class Margins
{
    public float Top { get; set; } = 20;
    public float Bottom { get; set; } = 20;
    public float Left { get; set; } = 20;
    public float Right { get; set; } = 20;
}

/// <summary>
/// Document section (header, content, footer)
/// </summary>
public class DocumentSection
{
    public string Name { get; set; } = string.Empty; // "header", "content", "footer"
    public string RenderedContent { get; set; } = string.Empty; // HTML from Scriban
}

/// <summary>
/// Document generation options
/// </summary>
public class DocumentOptions
{
    public bool IncludePageNumbers { get; set; } = true;
    public string PageNumberFormat { get; set; } = "Page {page} of {total}";
    public string? Watermark { get; set; }
    public bool CompressPdf { get; set; } = true;
}
