namespace Reef.Core.Models;

/// <summary>
/// Mapping of a source column to a target database column
/// </summary>
public class ImportColumnMapping
{
    /// <summary>Column name in source file / API response</summary>
    public required string SourceColumn { get; set; }

    /// <summary>Column name in target database table</summary>
    public required string TargetColumn { get; set; }

    /// <summary>Optional type cast hint: int, decimal, datetime, bool, string</summary>
    public string? DataType { get; set; }

    /// <summary>Default value if source value is null or missing</summary>
    public string? DefaultValue { get; set; }

    /// <summary>Optional Scriban expression applied to the value e.g. "{{ value | upcase }}"</summary>
    public string? Transformation { get; set; }

    /// <summary>Use this column as part of the upsert key (alternative to profile-level UpsertKeyColumns)</summary>
    public bool IsKeyColumn { get; set; } = false;

    /// <summary>Exclude column from INSERT/UPDATE when value is null</summary>
    public bool SkipOnNull { get; set; } = false;
}

/// <summary>
/// Format-specific parsing options stored as JSON in ImportProfile.FormatConfig
/// </summary>
public class ImportFormatConfig
{
    // CSV / TSV ──
    public string Delimiter { get; set; } = ",";
    public bool HasHeader { get; set; } = true;
    public string Encoding { get; set; } = "UTF-8";
    public string QuoteChar { get; set; } = "\"";
    public bool TrimWhitespace { get; set; } = true;
    public int SkipRows { get; set; } = 0;
    public string? NullValue { get; set; }

    // JSON / JSONL ──
    /// <summary>JSONPath expression pointing to the array of records e.g. "$.data"</summary>
    public string? DataRootPath { get; set; }
    public bool IsJsonLines { get; set; } = false;

    // XML ──
    /// <summary>XPath to the repeating record element e.g. "//Order"</summary>
    public string? RecordElement { get; set; }
    public string? XmlNamespace { get; set; }

    // Common ──
    public string? DateTimeFormat { get; set; }
    public string DecimalSeparator { get; set; } = ".";
    public string ThousandsSeparator { get; set; } = "";
}

/// <summary>
/// Pagination configuration for HTTP API sources
/// </summary>
public class ImportPaginationConfig
{
    /// <summary>Pagination type: None, Offset, Page, Cursor, Link</summary>
    public string Type { get; set; } = "None";

    // Offset / Page pagination
    public string? PageParam { get; set; }
    public string? LimitParam { get; set; }
    public int Limit { get; set; } = 100;
    public int MaxPages { get; set; } = 1000;

    // Cursor pagination
    public string? CursorPath { get; set; }
    public string? CursorParam { get; set; }

    // Link-header / next-URL pagination
    public string? NextLinkPath { get; set; }

    /// <summary>JSONPath to total count in response for progress logging</summary>
    public string? TotalCountPath { get; set; }

    public bool StopOnEmptyPage { get; set; } = true;
}

/// <summary>
/// Context passed to IImportTarget.WriteBatchAsync
/// </summary>
public class ImportWriteContext
{
    /// <summary>Target type: Database or LocalFile</summary>
    public string TargetType { get; set; } = "Database";

    // Database target 
    public Connection? TargetConnection { get; set; }
    public string TargetTable { get; set; } = "";
    public string LoadStrategy { get; set; } = "Upsert";
    public List<string> UpsertKeyColumns { get; set; } = new();
    public List<ImportColumnMapping> ColumnMappings { get; set; } = new();
    public int CommandTimeoutSeconds { get; set; } = 120;
    public string OnRowFailure { get; set; } = "SkipRow";
    public string OnConstraintViolation { get; set; } = "SkipRow";
    public int BatchSize { get; set; } = 500;

    // LocalFile target 
    public string? TargetFilePath { get; set; }
    public string? TargetFormat { get; set; } = "CSV";
    public string? TargetWriteMode { get; set; } = "Overwrite";
}

/// <summary>
/// Result from writing a batch of rows to the target database
/// </summary>
public class ImportBatchResult
{
    public int RowsInserted { get; set; }
    public int RowsUpdated { get; set; }
    public int RowsSkipped { get; set; }
    public int RowsFailed { get; set; }
    public List<ImportRowError> Errors { get; set; } = new();
}

/// <summary>
/// A single row-level write failure
/// </summary>
public class ImportRowError
{
    public int RowNumber { get; set; }
    public string? ReefId { get; set; }
    public required string ErrorMessage { get; set; }
    public string ErrorType { get; set; } = "Unknown";
    public Dictionary<string, object?>? RowData { get; set; }
}

/// <summary>
/// Schema information for a single column in the target table
/// </summary>
public class TargetColumnInfo
{
    public required string ColumnName { get; set; }
    public required string DataType { get; set; }
    public bool IsNullable { get; set; }
    public bool IsPrimaryKey { get; set; }
    public int? MaxLength { get; set; }
    public int? Precision { get; set; }
    public int? Scale { get; set; }
}

/// <summary>
/// A file or data stream fetched from an import source
/// </summary>
public class ImportSourceFile
{
    public required string Identifier { get; set; }
    public required Stream Content { get; set; }
    public long? SizeBytes { get; set; }
    public DateTime? LastModified { get; set; }
    public string? ContentType { get; set; }
}

/// <summary>
/// A single row produced by an import parser
/// </summary>
public class ParsedRow
{
    public int LineNumber { get; set; }
    public Dictionary<string, object?> Columns { get; set; } = new();
    public string? ParseError { get; set; }
    public bool IsSkipped { get; set; }
}

public class SourceFileInfo
{
    public required string Name { get; set; }
    public required string Path { get; set; }
    public long? SizeBytes { get; set; }
    public DateTime? LastModified { get; set; }
}
