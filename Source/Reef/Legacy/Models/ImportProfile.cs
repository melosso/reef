using Reef.Core.Models;

namespace Reef.Legacy.Models;

/// <summary>
/// Import Profile - reads data from a source (Local/SFTP/HTTP) and writes into a database Connection.
/// The inverse of an Export Profile.
/// </summary>
public class ImportProfile
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int? GroupId { get; set; }

    // SOURCE
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

    // FORMAT
    /// <summary>Source data format: CSV, JSON, JSONL, XML, YAML, TSV</summary>
    public string SourceFormat { get; set; } = "CSV";

    /// <summary>JSON-serialised ImportFormatConfig</summary>
    public string? FormatConfig { get; set; }

    // COLUMN MAPPING 
    /// <summary>JSON-serialised List&lt;ImportColumnMapping&gt;</summary>
    public string? ColumnMappingsJson { get; set; }

    /// <summary>Auto-match source columns to target columns by name (case-insensitive)</summary>
    public bool AutoMapColumns { get; set; } = true;

    /// <summary>Silently ignore source columns that have no mapping or target column</summary>
    public bool SkipUnmappedColumns { get; set; } = true;

    // TARGET
    /// <summary>Target type: Database (default) or LocalFile</summary>
    public string TargetType { get; set; } = "Database";

    /// <summary>FK to Connections table (target database), required when TargetType=Database, NULL for LocalFile</summary>
    public int? TargetConnectionId { get; set; }

    /// <summary>Target table name, required when TargetType=Database</summary>
    public required string TargetTable { get; set; }

    // LOCAL FILE TARGET 
    /// <summary>Absolute path to write output file, required when TargetType=LocalFile</summary>
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

    // DELTA SYNC
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

    // FAILURE HANDLING 
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

    // PRE/POST PROCESSING 
    public string? PreProcessType { get; set; }
    public string? PreProcessConfig { get; set; }
    public bool PreProcessRollbackOnFailure { get; set; } = true;

    public string? PostProcessType { get; set; }
    public string? PostProcessConfig { get; set; }
    public bool PostProcessSkipOnFailure { get; set; } = true;
    public bool PostProcessRollbackOnFailure { get; set; } = false;

    // NOTIFICATIONS
    public string? NotificationConfig { get; set; }
    public string? CanvasLayoutJson { get; set; }

    // METADATA
    public bool IsEnabled { get; set; } = true;
    public required string Hash { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int? CreatedBy { get; set; }
    public DateTime? LastExecutedAt { get; set; }

    // Unique human-readable short code, e.g. "I-B7K2" (set on creation, immutable)
    public string Code { get; set; } = "";
}
