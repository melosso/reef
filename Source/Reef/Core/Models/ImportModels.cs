namespace Reef.Core.Models;

// ===== Import Profile Core Model =====

/// <summary>
/// Import Profile - reads data from a source (Local/SFTP/HTTP) and writes into a database Connection.
/// The inverse of an Export Profile.
/// </summary>
public class ImportProfile
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int? GroupId { get; set; }

    // ── SOURCE ──────────────────────────────────────────────
    /// <summary>Source type: Local, Sftp, Ftp, Http</summary>
    public string SourceType { get; set; } = "Local";

    /// <summary>Optional FK to Destinations table (saved source config)</summary>
    public int? SourceDestinationId { get; set; }

    /// <summary>Inline source config JSON (encrypted if contains credentials)</summary>
    public string? SourceConfig { get; set; }

    /// <summary>Exact file path or pattern on the source</summary>
    public string? SourceFilePath { get; set; }

    /// <summary>Glob pattern for file selection e.g. "*.csv", "orders_*.json"</summary>
    public string? SourceFilePattern { get; set; }

    /// <summary>Which file(s) to pick: Latest, Oldest, All, Exact</summary>
    public string SourceFileSelection { get; set; } = "Latest";

    /// <summary>Move processed file to archive folder after successful import</summary>
    public bool ArchiveAfterImport { get; set; } = false;

    /// <summary>Subfolder/path for archiving processed files</summary>
    public string? ArchivePath { get; set; }

    // HTTP-specific source options
    public string? HttpMethod { get; set; } = "GET";
    public string? HttpBodyTemplate { get; set; }
    public bool HttpPaginationEnabled { get; set; } = false;
    public string? HttpPaginationConfig { get; set; }

    /// <summary>JSONPath or XPath to the data array in the response e.g. "$.data"</summary>
    public string? HttpDataRootPath { get; set; }

    // ── FORMAT ──────────────────────────────────────────────
    /// <summary>Source data format: CSV, JSON, JSONL, XML, YAML, TSV</summary>
    public string SourceFormat { get; set; } = "CSV";

    /// <summary>JSON-serialised ImportFormatConfig</summary>
    public string? FormatConfig { get; set; }

    // ── COLUMN MAPPING ─────────────────────────────────────
    /// <summary>JSON-serialised List&lt;ImportColumnMapping&gt;</summary>
    public string? ColumnMappingsJson { get; set; }

    /// <summary>Auto-match source columns to target columns by name (case-insensitive)</summary>
    public bool AutoMapColumns { get; set; } = true;

    /// <summary>Silently ignore source columns that have no mapping or target column</summary>
    public bool SkipUnmappedColumns { get; set; } = true;

    // ── TARGET ──────────────────────────────────────────────
    /// <summary>Target type: Database (default) or LocalFile</summary>
    public string TargetType { get; set; } = "Database";

    /// <summary>FK to Connections table (target database) — required when TargetType=Database, NULL for LocalFile</summary>
    public int? TargetConnectionId { get; set; }

    /// <summary>Target table name — required when TargetType=Database</summary>
    public required string TargetTable { get; set; }

    // ── LOCAL FILE TARGET ────────────────────────────────────
    /// <summary>Absolute path to write output file — required when TargetType=LocalFile</summary>
    public string? LocalTargetPath { get; set; }

    /// <summary>Output format for LocalFile target: CSV, JSON, JSONL</summary>
    public string? LocalTargetFormat { get; set; } = "CSV";

    /// <summary>Write mode for LocalFile target: Overwrite, Append</summary>
    public string? LocalTargetWriteMode { get; set; } = "Overwrite";

    /// <summary>Load strategy: Insert, Upsert, FullReplace, Append</summary>
    public string LoadStrategy { get; set; } = "Upsert";

    /// <summary>Comma-separated column names used for upsert key matching</summary>
    public string? UpsertKeyColumns { get; set; }

    /// <summary>Rows per database batch operation</summary>
    public int BatchSize { get; set; } = 500;

    /// <summary>DB command timeout in seconds</summary>
    public int CommandTimeoutSeconds { get; set; } = 120;

    // ── DELTA SYNC ──────────────────────────────────────────
    /// <summary>Enable delta sync - only import new/changed rows</summary>
    public bool DeltaSyncEnabled { get; set; } = false;

    /// <summary>Column in SOURCE data that uniquely identifies each row</summary>
    public string? DeltaSyncReefIdColumn { get; set; }

    public string? DeltaSyncHashAlgorithm { get; set; } = "SHA256";

    /// <summary>Track rows that disappear from the source and apply delete strategy</summary>
    public bool DeltaSyncTrackDeletes { get; set; } = false;

    /// <summary>How to handle deleted rows: SoftDelete, HardDelete, None</summary>
    public string? DeltaSyncDeleteStrategy { get; set; } = "SoftDelete";

    /// <summary>Column to update on soft-delete (e.g. "IsDeleted")</summary>
    public string? DeltaSyncDeleteColumn { get; set; }

    /// <summary>Value to set on soft-delete (e.g. "1")</summary>
    public string? DeltaSyncDeleteValue { get; set; }

    public int? DeltaSyncRetentionDays { get; set; }

    // ── FAILURE HANDLING ────────────────────────────────────
    /// <summary>What to do when source fetch fails: Fail, Skip, Retry</summary>
    public string OnSourceFailure { get; set; } = "Fail";

    /// <summary>What to do when a row fails to parse: Fail, SkipRow, SkipFile</summary>
    public string OnParseFailure { get; set; } = "SkipRow";

    /// <summary>What to do when a DB write row fails: Fail, SkipRow, Rollback</summary>
    public string OnRowFailure { get; set; } = "SkipRow";

    /// <summary>What to do on DB constraint violation: Fail, SkipRow, Overwrite</summary>
    public string OnConstraintViolation { get; set; } = "SkipRow";

    /// <summary>Abort execution if this many rows fail (0 = unlimited)</summary>
    public int MaxFailedRowsBeforeAbort { get; set; } = 0;

    /// <summary>Abort execution if this % of rows fail (0 = unlimited)</summary>
    public int MaxFailedRowsPercent { get; set; } = 0;

    /// <summary>Rollback all DB writes when execution aborts mid-way</summary>
    public bool RollbackOnAbort { get; set; } = true;

    /// <summary>Max source connection retries with exponential back-off</summary>
    public int RetryCount { get; set; } = 3;

    // ── PRE/POST PROCESSING ──────────────────────────────────
    public string? PreProcessType { get; set; }
    public string? PreProcessConfig { get; set; }
    public bool PreProcessRollbackOnFailure { get; set; } = true;

    public string? PostProcessType { get; set; }
    public string? PostProcessConfig { get; set; }
    public bool PostProcessSkipOnFailure { get; set; } = true;
    public bool PostProcessRollbackOnFailure { get; set; } = false;

    // ── NOTIFICATIONS ───────────────────────────────────────
    public string? NotificationConfig { get; set; }

    // ── METADATA ────────────────────────────────────────────
    public bool IsEnabled { get; set; } = true;
    public required string Hash { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int? CreatedBy { get; set; }
    public DateTime? LastExecutedAt { get; set; }
}

/// <summary>
/// Extended view of ImportProfile with joined name lookups
/// </summary>
public class ImportProfileWithNames : ImportProfile
{
    public string? GroupName { get; set; }
    public string? TargetConnectionName { get; set; }
    public string? TargetConnectionType { get; set; }
    public string? SourceDestinationName { get; set; }
}

// ===== Import Execution Models =====

/// <summary>
/// Execution record for an import profile run — mirrors ImportProfileExecutions table schema.
/// </summary>
public class ImportProfileExecution
{
    public int Id { get; set; }
    public int ImportProfileId { get; set; }

    /// <summary>Running, Success, PartialSuccess, Failed</summary>
    public string Status { get; set; } = "Running";
    public string? TriggeredBy { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }

    // Row counters
    public int TotalRowsRead { get; set; }
    public int RowsInserted { get; set; }
    public int RowsUpdated { get; set; }
    public int RowsSkipped { get; set; }
    public int RowsDeleted { get; set; }
    public int RowsFailed { get; set; }

    // Progress / Diagnostics
    public string? CurrentPhase { get; set; }
    public string? ErrorMessage { get; set; }
    public string? StackTrace { get; set; }
    public string? ExecutionLog { get; set; }
    public int FilesProcessed { get; set; }
    public long BytesProcessed { get; set; }

    // Delta sync counters
    public int DeltaSyncNewRows { get; set; }
    public int DeltaSyncChangedRows { get; set; }
    public int DeltaSyncUnchangedRows { get; set; }
    public int DeltaSyncDeletedRows { get; set; }

    /// <summary>JSON object keyed by phase name, value = elapsed ms</summary>
    public string? PhaseTimingsJson { get; set; }
}

/// <summary>
/// Individual row-level write error stored for audit/review — mirrors ImportExecutionErrors table.
/// </summary>
public class ImportExecutionError
{
    public int Id { get; set; }
    public int ExecutionId { get; set; }
    public int? RowNumber { get; set; }
    public string? ReefId { get; set; }
    /// <summary>Constraint, Type, Timeout, Parse, Unknown</summary>
    public string ErrorType { get; set; } = "Unknown";
    public required string ErrorMessage { get; set; }
    public string? RowDataJson { get; set; }
    /// <summary>Phase in which the error occurred</summary>
    public string? Phase { get; set; }
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}

// ===== Delta Sync Stats =====

public record ImportDeltaSyncStats
{
    public int ActiveRows { get; init; }
    public int DeletedRows { get; init; }
    public int TotalTrackedRows { get; init; }
    public string? LastSyncDate { get; init; }
    public int NewRowsLastRun { get; init; }
    public int ChangedRowsLastRun { get; init; }
    public int DeletedRowsLastRun { get; init; }
    public int UnchangedRowsLastRun { get; init; }
}

// ===== Configuration Models =====

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
    // ── CSV / TSV ──
    public string Delimiter { get; set; } = ",";
    public bool HasHeader { get; set; } = true;
    public string Encoding { get; set; } = "UTF-8";
    public string QuoteChar { get; set; } = "\"";
    public bool TrimWhitespace { get; set; } = true;
    public int SkipRows { get; set; } = 0;
    public string? NullValue { get; set; }

    // ── JSON / JSONL ──
    /// <summary>JSONPath expression pointing to the array of records e.g. "$.data"</summary>
    public string? DataRootPath { get; set; }
    public bool IsJsonLines { get; set; } = false;

    // ── XML ──
    /// <summary>XPath to the repeating record element e.g. "//Order"</summary>
    public string? RecordElement { get; set; }
    public string? XmlNamespace { get; set; }

    // ── Common ──
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

// ===== Write Context Models =====

/// <summary>
/// Context passed to IImportTarget.WriteBatchAsync
/// </summary>
public class ImportWriteContext
{
    /// <summary>Target type: Database or LocalFile</summary>
    public string TargetType { get; set; } = "Database";

    // ── Database target ──────────────────────────────────────
    public Connection? TargetConnection { get; set; }
    public string TargetTable { get; set; } = "";
    public string LoadStrategy { get; set; } = "Upsert";
    public List<string> UpsertKeyColumns { get; set; } = new();
    public List<ImportColumnMapping> ColumnMappings { get; set; } = new();
    public int CommandTimeoutSeconds { get; set; } = 120;
    public string OnRowFailure { get; set; } = "SkipRow";
    public string OnConstraintViolation { get; set; } = "SkipRow";
    public int BatchSize { get; set; } = 500;

    // ── LocalFile target ─────────────────────────────────────
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

// ===== Source File Model =====

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

// ===== Parsed Row Model =====

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

// ===== API Request/Response Models =====

/// <summary>
/// Request body for testing import source connectivity
/// </summary>
public class TestImportSourceRequest
{
    public string SourceType { get; set; } = "Local";
    public string? SourceConfig { get; set; }
    public int? SourceDestinationId { get; set; }
    public string? SourceFilePath { get; set; }
    public string? SourceFilePattern { get; set; }
    public string SourceFileSelection { get; set; } = "Latest";
}

/// <summary>
/// Request body for getting target table schema
/// </summary>
public class GetTargetSchemaRequest
{
    public string TargetType { get; set; } = "Database";
    public int? TargetConnectionId { get; set; }
    public required string TargetTable { get; set; }
}

/// <summary>
/// Request body for previewing parsed rows without writing
/// </summary>
public class PreviewImportRequest
{
    public int? ImportProfileId { get; set; }
    public string SourceType { get; set; } = "Local";
    public string? SourceConfig { get; set; }
    public int? SourceDestinationId { get; set; }
    public string? SourceFilePath { get; set; }
    public string? SourceFilePattern { get; set; }
    public string SourceFileSelection { get; set; } = "Latest";
    public string SourceFormat { get; set; } = "CSV";
    public string? FormatConfig { get; set; }
    public int MaxRows { get; set; } = 10;

    // HTTP source fields
    public string? HttpMethod { get; set; }
    public string? HttpBodyTemplate { get; set; }
    public bool HttpPaginationEnabled { get; set; }
    public string? HttpPaginationConfig { get; set; }
    public string? HttpDataRootPath { get; set; }
}

/// <summary>
/// Response from the preview endpoint
/// </summary>
public class PreviewImportResponse
{
    public bool Success { get; set; }
    public List<Dictionary<string, object?>> Rows { get; set; } = new();
    public List<string> Columns { get; set; } = new();
    public int TotalRowsParsed { get; set; }
    public int RowsReturned { get; set; }
    public string? ErrorMessage { get; set; }
    public List<string> ParseWarnings { get; set; } = new();
}

/// <summary>
/// Response from listing source files
/// </summary>
public class ListSourceFilesResponse
{
    public bool Success { get; set; }
    public List<SourceFileInfo> Files { get; set; } = new();
    public string? ErrorMessage { get; set; }
}

public class SourceFileInfo
{
    public required string Name { get; set; }
    public required string Path { get; set; }
    public long? SizeBytes { get; set; }
    public DateTime? LastModified { get; set; }
}
