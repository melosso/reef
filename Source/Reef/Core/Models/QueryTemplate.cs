namespace Reef.Core.Models;

public class QueryTemplate
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public QueryTemplateType Type { get; set; }
    public string Template { get; set; } = string.Empty;
    public string OutputFormat { get; set; } = "XML"; // XML, JSON, CSV
    public bool IsActive { get; set; } = true;
    public string Tags { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ModifiedAt { get; set; }
    public string Hash { get; set; } = string.Empty;
}

public enum QueryTemplateType
{
    XmlTemplate = 1,      // Custom XML template (legacy - hardcoded structure)
    JsonTemplate = 2,     // Custom JSON template (legacy - hardcoded structure)
    XsltTransform = 3,    // XSLT transformation
    ForXmlRaw = 4,        // FOR XML RAW
    ForXmlAuto = 5,       // FOR XML AUTO
    ForXmlPath = 6,       // FOR XML PATH
    ForXmlExplicit = 7,   // FOR XML EXPLICIT
    ForJson = 8,          // FOR JSON AUTO
    ForJsonPath = 9,      // FOR JSON PATH
    ScribanTemplate = 10  // Scriban template engine (supports any output format: CSV, XML, JSON, etc.)
}

public class ProfileTransformation
{
    public int Id { get; set; }
    public int ProfileId { get; set; }
    public int? TemplateId { get; set; } // null for inline templates
    public string? InlineTemplate { get; set; }
    public QueryTemplateType TransformationType { get; set; }
    public int SortOrder { get; set; } // For chaining multiple transformations
    public bool IsEnabled { get; set; } = true;
    public string? TransformationOptions { get; set; } // JSON for additional options
}

public class ForXmlOptions
{
    public string? RootElement { get; set; }
    public string? RowElement { get; set; }
    public bool ElementsMode { get; set; } = false; // ELEMENTS vs attributes
    public bool XsiNil { get; set; } = false;
    public string? XmlSchema { get; set; }
    public bool BinaryBase64 { get; set; } = true;
    public string? Type { get; set; } // RAW, AUTO, PATH, EXPLICIT
}

public class ForJsonOptions
{
    public bool IncludeNullValues { get; set; } = false;
    public bool AutoArray { get; set; } = true;
    public string? RootElement { get; set; }
    public bool WithoutArrayWrapper { get; set; } = false;
    public string? Path { get; set; }
}

public class TransformationRequest
{
    public string Query { get; set; } = string.Empty;
    public int? TemplateId { get; set; }
    public string? InlineTemplate { get; set; }
    public QueryTemplateType Type { get; set; }
    public Dictionary<string, object>? Options { get; set; }
}

public class TransformationResult
{
    public bool Success { get; set; }
    public string? Output { get; set; }
    public string? Format { get; set; }
    public int RowCount { get; set; }
    public string? ErrorMessage { get; set; }
    public TimeSpan Duration { get; set; }
}