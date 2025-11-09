namespace Reef.Core.Models;

/// <summary>
/// User entity for authentication and authorization
/// </summary>
public class User
{
    public int Id { get; set; }
    public required string Username { get; set; }
    public required string PasswordHash { get; set; }
    public string Role { get; set; } = "User";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public bool PasswordChangeRequired { get; set; } = false;
}

/// <summary>
/// API key entity for programmatic access
/// </summary>
public class ApiKey
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string KeyHash { get; set; }
    public required string Permissions { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Profile group entity for organizing profiles into folders
/// </summary>
public class ProfileGroup
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int? ParentId { get; set; }
    public string? Description { get; set; }
    public int SortOrder { get; set; } = 0;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Connection entity for database connection strings
/// </summary>
public class Connection
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Type { get; set; } // SqlServer, MySQL, PostgreSQL
    public required string ConnectionString { get; set; } // Encrypted with PWENC:
    public bool IsActive { get; set; } = true;
    public string? Tags { get; set; } // JSON array
    public required string Hash { get; set; } // SHA256 for tamper detection
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? LastTestedAt { get; set; }
    public string? LastTestResult { get; set; }
}

/// <summary>
/// Profile entity for query execution configuration
/// </summary>
public class Profile
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int ConnectionId { get; set; }
    public int? GroupId { get; set; }
    public required string Query { get; set; }
    public string? ScheduleType { get; set; } // null, Cron, Interval, Webhook
    public string? ScheduleCron { get; set; }
    public int? ScheduleIntervalMinutes { get; set; }
    public string OutputFormat { get; set; } = "JSON"; // JSON, XML, CSV, TAB, YAML
    public string OutputDestinationType { get; set; } = "Local"; // Local, FTP, SFTP, S3, Azure
    public string? OutputDestinationConfig { get; set; } // JSON configuration
    public string? OutputPropertiesJson { get; set; } // Additional output properties
    public int? OutputDestinationId { get; set; } // FK to Destinations table (optional)
    public int? TemplateId { get; set; } // FK to QueryTemplates table (optional - for custom transformations)
    public string? TransformationOptionsJson { get; set; } // JSON options for SQL Server native transformations (ForJsonOptions, ForXmlOptions)
    
    // Pre-Processing Configuration
    public string? PreProcessType { get; set; } // null, Query, StoredProcedure
    public string? PreProcessConfig { get; set; } // JSON configuration (ProcessingConfig)
    public bool PreProcessRollbackOnFailure { get; set; } = true; // Rollback pre-processing if main query fails
    
    // Post-Processing Configuration (enhanced existing)
    public string? PostProcessType { get; set; } // null, Query, StoredProcedure, Webhook
    public string? PostProcessConfig { get; set; } // JSON configuration (ProcessingConfig)
    public bool PostProcessSkipOnFailure { get; set; } = true; // Skip post-processing if main query fails
    public bool PostProcessRollbackOnFailure { get; set; } = false; // Rollback post-processing on its own failure
    public bool PostProcessOnZeroRows { get; set; } = false; // Run post-processing even when query returns 0 rows (opt-in)
    
    public string? NotificationConfig { get; set; } // JSON configuration
    public string? DependsOnProfileIds { get; set; } // Comma-separated profile IDs for dependencies
    public bool IsEnabled { get; set; } = true;
    public required string Hash { get; set; } // SHA256 for tamper detection
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? LastExecutedAt { get; set; }
    
    // Delta Sync Configuration - Basic
    public bool DeltaSyncEnabled { get; set; } = false;
    public string? DeltaSyncReefIdColumn { get; set; }
    public string? DeltaSyncHashAlgorithm { get; set; } = "SHA256";
    public bool DeltaSyncTrackDeletes { get; set; } = false;
    public int? DeltaSyncRetentionDays { get; set; }
    
    // Delta Sync Configuration - Edge Case Handling
    public string? DeltaSyncDuplicateStrategy { get; set; } = "Strict"; // Strict, Composite, Skip
    public string? DeltaSyncNullStrategy { get; set; } = "Strict"; // Strict, Skip, Generate
    public bool DeltaSyncResetOnSchemaChange { get; set; } = false;
    public int? DeltaSyncNumericPrecision { get; set; } = 6; // Decimal places for floating point
    public bool DeltaSyncRemoveNonPrintable { get; set; } = false; // Strip non-printable chars
    public string? DeltaSyncReefIdNormalization { get; set; } = "Trim"; // Trim, Lowercase, RemoveWhitespace
    
    // Advanced Output Options
    public bool ExcludeReefIdFromOutput { get; set; } = true; // Don't include ReefId column in exports (internal use only)
    public bool ExcludeSplitKeyFromOutput { get; set; } = false; // Don't include SplitKey column in exports (internal use only)

    // Filename override for non-split executions
    public string? FilenameTemplate { get; set; } = "{profile}_{timestamp}.{format}";

    public bool SplitEnabled { get; set; } = false; // Enable multi-output splitting
    public string? SplitKeyColumn { get; set; } // Column name to group rows by (e.g., CustomerId, DebtorCode)
    public string? SplitFilenameTemplate { get; set; } = "{profile}_{splitkey}_{timestamp}.{format}"; // Filename template for split files
    public int SplitBatchSize { get; set; } = 1; // Number of rows per file (1 = one file per split key, N = batch N rows per file)
    public bool PostProcessPerSplit { get; set; } = false; // Run post-processing for each split
}

/// <summary>
/// Profile execution history entity
/// </summary>
public class ProfileExecution
{
    public int Id { get; set; }
    public int ProfileId { get; set; }
    public int? JobId { get; set; } // Optional: Which job triggered this execution
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public string Status { get; set; } = "Running"; // Running, Success, Failed
    public int? RowCount { get; set; }
    public string? OutputPath { get; set; }
    public long? FileSizeBytes { get; set; }
    public string? ErrorMessage { get; set; }
    public string? OutputMessage { get; set; } // Success messages like HTTP responses
    public long? ExecutionTimeMs { get; set; }
    public string? TriggeredBy { get; set; }
    
    // Pre-Processing Phase Tracking
    public DateTime? PreProcessStartedAt { get; set; }
    public DateTime? PreProcessCompletedAt { get; set; }
    public string? PreProcessStatus { get; set; } // null, Success, Failed, Skipped
    public string? PreProcessError { get; set; }
    public long? PreProcessTimeMs { get; set; }
    
    // Post-Processing Phase Tracking
    public DateTime? PostProcessStartedAt { get; set; }
    public DateTime? PostProcessCompletedAt { get; set; }
    public string? PostProcessStatus { get; set; } // null, Success, Failed, Skipped
    public string? PostProcessError { get; set; }
    public long? PostProcessTimeMs { get; set; }
    
    // Delta Sync Metrics
    public int? DeltaSyncNewRows { get; set; }
    public int? DeltaSyncChangedRows { get; set; }
    public int? DeltaSyncDeletedRows { get; set; }
    public int? DeltaSyncUnchangedRows { get; set; }
    public int? DeltaSyncTotalHashedRows { get; set; }
    
    // Multi-Output Splitting Tracking
    public bool WasSplit { get; set; } = false; // Whether this execution used multi-output splitting
    public int? SplitCount { get; set; } // Total number of splits generated
    public int? SplitSuccessCount { get; set; } // Number of splits that succeeded
    public int? SplitFailureCount { get; set; } // Number of splits that failed
}

/// <summary>
/// Paged result for executions
/// </summary>
public class ExecutionPagedResult
{
    public List<ProfileExecution> Data { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages { get; set; }
}

/// <summary>
/// Represents a single split from a multi-output profile execution
/// </summary>
public class ProfileExecutionSplit
{
    public int Id { get; set; }
    public int ExecutionId { get; set; }
    public string SplitKey { get; set; } = string.Empty;
    public int RowCount { get; set; }
    public string Status { get; set; } = "Running"; // Running, Success, Failed
    public string? OutputPath { get; set; }
    public long? FileSizeBytes { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}

/// <summary>
/// Webhook trigger entity for external integrations
/// </summary>
public class WebhookTrigger
{
    public int Id { get; set; }
    public int? ProfileId { get; set; } // Nullable to support Job webhooks
    public int? JobId { get; set; } // Nullable to support Profile webhooks
    public required string Token { get; set; } // Unique webhook token
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastTriggeredAt { get; set; }
    public int TriggerCount { get; set; } = 0;
}

/// <summary>
/// Audit log entity for compliance tracking
/// </summary>
public class AuditLog
{
    public int Id { get; set; }
    public required string EntityType { get; set; } // Connection, Profile, User, etc.
    public int EntityId { get; set; }
    public required string Action { get; set; } // Created, Updated, Deleted, Executed
    public required string PerformedBy { get; set; }
    public string? Changes { get; set; } // JSON diff
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
}

/// <summary>
/// Profile dependency entity for execution chains
/// </summary>
public class ProfileDependency
{
    public int Id { get; set; }
    public int ProfileId { get; set; }
    public int DependsOnProfileId { get; set; }
    public int ExecutionOrder { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Scheduled task entity for background scheduler
/// </summary>
public class ScheduledTask
{
    public int Id { get; set; }
    public int ProfileId { get; set; }
    public DateTime NextRunAt { get; set; }
    public DateTime? LastRunAt { get; set; }
    public bool IsRunning { get; set; } = false;
    public int FailureCount { get; set; } = 0;
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

// ===== Pre/Post-Processing Configuration Models =====

/// <summary>
/// Configuration for pre-processing or post-processing execution
/// Stored as JSON in Profile.PreProcessConfig or Profile.PostProcessConfig
/// </summary>
public class ProcessingConfig
{
    /// <summary>
    /// Type of processing: Query or StoredProcedure
    /// </summary>
    public required string Type { get; set; } // Query, StoredProcedure
    
    /// <summary>
    /// SQL command to execute (can contain {placeholder} variables)
    /// For Query: SELECT, UPDATE, DELETE, INSERT, etc.
    /// For StoredProcedure: Procedure name (without EXEC/CALL)
    /// </summary>
    public required string Command { get; set; }
    
    /// <summary>
    /// Optional parameters for stored procedure or parameterized query
    /// </summary>
    public List<ProcessingParameter>? Parameters { get; set; }
    
    /// <summary>
    /// Timeout in seconds (default 30)
    /// </summary>
    public int Timeout { get; set; } = 30;
    
    /// <summary>
    /// Whether to continue execution if this processing step fails
    /// </summary>
    public bool ContinueOnError { get; set; } = false;
}

/// <summary>
/// Parameter for stored procedure or parameterized query
/// </summary>
public class ProcessingParameter
{
    /// <summary>
    /// Parameter name (e.g., "@userId", "p_userId")
    /// </summary>
    public required string Name { get; set; }
    
    /// <summary>
    /// Parameter value (can be literal or {placeholder} variable)
    /// </summary>
    public required string Value { get; set; }
    
    /// <summary>
    /// SQL data type (e.g., "INT", "VARCHAR(50)", "DATETIME")
    /// Optional - used for explicit type casting
    /// </summary>
    public string? Type { get; set; }
}

/// <summary>
/// Context data available for variable substitution in processing commands
/// Variables can be used in ProcessingConfig.Command and ProcessingParameter.Value
/// Example: "UPDATE log SET row_count = {rowcount}, exec_id = {executionid}"
/// </summary>
public class ProcessingContext
{
    /// <summary>
    /// Current execution ID
    /// Available as: {executionid}
    /// </summary>
    public int ExecutionId { get; set; }
    
    /// <summary>
    /// Profile ID being executed
    /// Available as: {profileid}
    /// </summary>
    public int ProfileId { get; set; }
    
    /// <summary>
    /// Number of rows returned by main query
    /// Available as: {rowcount}
    /// </summary>
    public int RowCount { get; set; }
    
    /// <summary>
    /// Output file path (for Local destinations)
    /// Available as: {outputpath}
    /// </summary>
    public string? OutputPath { get; set; }
    
    /// <summary>
    /// File size in bytes
    /// Available as: {filesizebytes}
    /// </summary>
    public long? FileSizeBytes { get; set; }
    
    /// <summary>
    /// Main query execution time in milliseconds
    /// Available as: {executiontimems}
    /// </summary>
    public long ExecutionTimeMs { get; set; }
    
    /// <summary>
    /// Output format (JSON, CSV, XML, etc.)
    /// Available as: {outputformat}
    /// </summary>
    public string OutputFormat { get; set; } = "JSON";
    
    /// <summary>
    /// Who/what triggered the execution
    /// Available as: {triggeredby}
    /// </summary>
    public string? TriggeredBy { get; set; }
    
    /// <summary>
    /// Execution start timestamp (ISO 8601 UTC)
    /// Available as: {startedat}
    /// </summary>
    public DateTime StartedAt { get; set; }
    
    /// <summary>
    /// Execution completion timestamp (ISO 8601 UTC)
    /// Available as: {completedat}
    /// </summary>
    public DateTime? CompletedAt { get; set; }
    
    /// <summary>
    /// Main query execution status (Success, Failed)
    /// Available as: {status}
    /// </summary>
    public string Status { get; set; } = "Running";
    
    /// <summary>
    /// Error message from main query (if failed)
    /// Available as: {errormessage}
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Delta Sync ReefId Column (if enabled)
    /// Available as: {deltasyncreefidcolumn}
    /// </summary>
    public string? DeltaSyncReefIdColumn { get; set; }

    /// <summary>
    /// Split Key Column (if enabled)
    /// Available as: {splitkeycolumn}
    /// </summary>
    public string? SplitKeyColumn { get; set; }

    /// <summary>
    /// Split Key Value (if per-split)
    /// Available as: {splitkey}
    /// </summary>
    public string? SplitKey { get; set; }
}

// ===== API Request/Response Models =====

/// <summary>
/// Login request
/// </summary>
public class LoginRequest
{
    public required string Username { get; set; }
    public required string Password { get; set; }
}

/// <summary>
/// Login response
/// </summary>
public class LoginResponse
{
    public required string Token { get; set; }
    public required string Username { get; set; }
    public required string Role { get; set; }
    public DateTime ExpiresAt { get; set; }
}

/// <summary>
/// Connection test request
/// </summary>
public class TestConnectionRequest
{
    public required string Type { get; set; }
    public required string ConnectionString { get; set; }
    public int? ConnectionId { get; set; } // Optional: if set, update LastTestedAt
}

/// <summary>
/// Connection test response
/// </summary>
public class TestConnectionResponse
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public long? ResponseTimeMs { get; set; }
}

/// <summary>
/// Profile execution request
/// </summary>
public class ExecuteProfileRequest
{
    public int ProfileId { get; set; }
    public Dictionary<string, string>? Parameters { get; set; }
    public string? TriggeredBy { get; set; }
}

/// <summary>
/// Profile execution response
/// </summary>
public class ExecuteProfileResponse
{
    public int ExecutionId { get; set; }
    public string Status { get; set; } = "Running";
    public string? OutputPath { get; set; }
}

// ===== Delta Sync Models =====

/// <summary>
/// Delta sync configuration
/// </summary>
public class DeltaSyncConfig
{
    public bool Enabled { get; set; }
    public required string ReefIdColumn { get; set; }
    public string HashAlgorithm { get; set; } = "SHA256";
    public bool IncludeDeleted { get; set; }
    public int? RetentionDays { get; set; }
    public string DuplicateStrategy { get; set; } = "Strict";
    public string NullStrategy { get; set; } = "Strict";
    public int NumericPrecision { get; set; } = 6;
    public bool RemoveNonPrintable { get; set; }
    public string ReefIdNormalization { get; set; } = "Trim";
}

/// <summary>
/// Delta sync processing result
/// </summary>
public class DeltaSyncResult
{
    public List<Dictionary<string, object>> NewRows { get; set; } = new();
    public List<Dictionary<string, object>> ChangedRows { get; set; } = new();
    public List<Dictionary<string, object>> UnchangedRows { get; set; } = new();
    public List<string> DeletedReefIds { get; set; } = new();
    public int TotalRowsProcessed { get; set; }
    public Dictionary<string, string> NewHashState { get; set; } = new();
}

/// <summary>
/// Criteria for resetting delta sync state
/// </summary>
public class DeltaSyncResetCriteria
{
    public DateTime? LastSeenBefore { get; set; }
    public DateTime? LastSeenAfter { get; set; }
    public int? LastSeenExecutionIdBefore { get; set; }
    public bool? IsDeleted { get; set; }
    public string? ReefIdPattern { get; set; }
}

/// <summary>
/// Delta sync statistics
/// </summary>
public class DeltaSyncStats
{
    public int ProfilesWithDeltaSync { get; set; }
    public int TotalHashedRows { get; set; }
    public int TotalDeletedRows { get; set; }
}

/// <summary>
/// Profile-specific delta sync statistics
/// </summary>
public class ProfileDeltaSyncStats
{
    public int ActiveRows { get; set; }
    public int DeletedRows { get; set; }
    public int TotalTrackedRows { get; set; }
    public DateTime? FirstTrackedAt { get; set; }
    public DateTime? LastTrackedAt { get; set; }
}

/// <summary>
/// Request to reset specific rows
/// </summary>
public class ResetRowsRequest
{
    public List<string>? ReefIds { get; set; }
    public DeltaSyncResetCriteria? Criteria { get; set; }
}

/// <summary>
/// Database configuration - used by all services
/// </summary>
public class DatabaseConfig
{
    public required string ConnectionString { get; init; }
}

// ===== IMPORT Models =====

/// <summary>
/// Enumeration for data source types (REST API, S3, FTP, Database, File, etc.)
/// </summary>
public enum DataSourceType
{
    Unknown,
    RestApi,
    S3,
    Ftp,
    Sftp,
    Database,
    File,
    AzureBlob,
    GoogleCloudStorage,
    Kafka,           // Future
    DatabaseCdc      // Future
}

/// <summary>
/// Enumeration for import destination types
/// </summary>
public enum ImportDestinationType
{
    Unknown,
    Database,
    File,
    S3,
    Ftp,
    Sftp,
    AzureBlob,
    GoogleCloudStorage,
    Kafka             // Future
}

/// <summary>
/// Error handling strategy for imports
/// </summary>
public enum ImportErrorStrategy
{
    Skip,             // Skip rows with errors, continue
    Fail,             // Fail entire import on any error
    Retry,            // Retry with exponential backoff
    Quarantine        // Write errors to quarantine location
}

/// <summary>
/// Delta sync mode for change detection
/// </summary>
public enum DeltaSyncMode
{
    None,             // No delta sync (full load each time)
    Hash,             // SHA256 hash of all columns
    Timestamp,        // Compare timestamp columns
    Key,              // Natural/synthetic key comparison
    Incremental       // Auto-increment or sequence ID
}

/// <summary>
/// Data type enumeration for field mapping
/// </summary>
public enum FieldDataType
{
    String,
    Integer,
    Decimal,
    DateTime,
    Boolean,
    Json,
    Binary
}

/// <summary>
/// Validation type enumeration
/// </summary>
public enum ValidationType
{
    Required,
    Regex,
    MinLength,
    MaxLength,
    MinValue,
    MaxValue,
    Enum,
    Custom
}

/// <summary>
/// Import Profile - defines the import configuration and data mapping
/// </summary>
public class ImportProfile
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }

    // Source configuration
    public int SourceConnectionId { get; set; }
    public DataSourceType SourceType { get; set; }
    public required string SourceUri { get; set; }           // API URL, S3 path, FTP path, table name, etc.
    public string? SourceConfiguration { get; set; }        // JSON: pagination, auth, filters

    // Destination configuration
    public int DestinationConnectionId { get; set; }
    public ImportDestinationType DestinationType { get; set; }
    public required string DestinationUri { get; set; }     // Table name, file path, S3 path
    public string? DestinationConfiguration { get; set; }   // JSON: format, options, upsert logic

    // Data mapping and validation
    public string? FieldMappingsJson { get; set; }          // JSON array of FieldMapping
    public string? ValidationRulesJson { get; set; }        // JSON array of ValidationRule

    // Scheduling
    public string? ScheduleType { get; set; }               // null, Cron, Interval, Webhook
    public string? ScheduleCron { get; set; }
    public int? ScheduleIntervalMinutes { get; set; }
    public string? WebhookSecret { get; set; }

    // Transformation
    public string? PreProcessTemplate { get; set; }         // Scriban template
    public string? PostProcessTemplate { get; set; }        // Scriban template

    // Error handling
    public ImportErrorStrategy ErrorStrategy { get; set; } = ImportErrorStrategy.Skip;
    public int MaxRetries { get; set; } = 3;
    public int RetryDelaySeconds { get; set; } = 5;

    // Delta sync
    public DeltaSyncMode DeltaSyncMode { get; set; } = DeltaSyncMode.None;
    public string? DeltaSyncKeyColumns { get; set; }        // Comma-separated column names
    public bool TrackChanges { get; set; } = false;

    // Logging and retention
    public bool LogDetailedErrors { get; set; } = false;
    public int ExecutionHistoryRetentionDays { get; set; } = 30;

    // Status
    public bool IsEnabled { get; set; } = true;
    public required string Hash { get; set; }                // SHA256 for integrity

    // Metadata
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime? LastExecutedAt { get; set; }
}

/// <summary>
/// Import Job - scheduling metadata for import execution
/// </summary>
public class ImportJob
{
    public int Id { get; set; }
    public int ImportProfileId { get; set; }
    public DateTime NextRunAt { get; set; }
    public DateTime? LastRunAt { get; set; }
    public bool IsRunning { get; set; } = false;
    public int FailureCount { get; set; } = 0;
    public string? LastError { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>
/// Import Execution - tracks a single import execution
/// </summary>
public class ImportExecution
{
    public int Id { get; set; }
    public int ImportProfileId { get; set; }
    public int? JobId { get; set; }

    // Timing
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public long? ExecutionTimeMs { get; set; }

    // Status
    public string Status { get; set; } = "Running";          // Running, Success, Failed

    // Metrics
    public int? RowsRead { get; set; }
    public int? RowsWritten { get; set; }
    public int? RowsSkipped { get; set; }
    public int? RowsFailed { get; set; }

    // Delta sync metrics (if enabled)
    public int? DeltaSyncNewRows { get; set; }
    public int? DeltaSyncChangedRows { get; set; }
    public int? DeltaSyncUnchangedRows { get; set; }

    // Error tracking
    public string? ErrorMessage { get; set; }
    public string? ErrorDetails { get; set; }               // JSON: detailed error information

    // Pipeline stage tracking
    public string? CurrentStage { get; set; }                // Validation, SourceRead, Transform, Validate, Write, Commit, Cleanup
    public string? StageDetails { get; set; }               // JSON: per-stage metrics

    // Audit
    public string? TriggeredBy { get; set; }
}

/// <summary>
/// Field mapping between source and destination columns
/// </summary>
public class FieldMapping
{
    public int Id { get; set; }
    public int ImportProfileId { get; set; }
    public required string SourceColumn { get; set; }
    public required string DestinationColumn { get; set; }
    public FieldDataType DataType { get; set; } = FieldDataType.String;
    public bool Required { get; set; } = false;
    public string? DefaultValue { get; set; }
    public string? TransformationTemplate { get; set; }     // Scriban for derived fields
}

/// <summary>
/// Validation rule for imported data
/// </summary>
public class ValidationRule
{
    public int Id { get; set; }
    public int ImportProfileId { get; set; }
    public required string ColumnName { get; set; }
    public ValidationType ValidationType { get; set; }
    public string? Pattern { get; set; }                    // Regex pattern
    public string? MinValue { get; set; }
    public string? MaxValue { get; set; }
    public string? AllowedValuesJson { get; set; }          // JSON array
    public string? ErrorMessage { get; set; }
}