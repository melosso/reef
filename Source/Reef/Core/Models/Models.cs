namespace Reef.Core.Models;

/// <summary>
/// User entity for authentication and authorization
/// </summary>
public class User
{
    public int Id { get; set; }
    public required string Username { get; set; }
    public required string PasswordHash { get; set; }
    public string? DisplayName { get; set; }
    public string Role { get; set; } = "User";
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLoginAt { get; set; }
    public DateTime? LastSeenAt { get; set; }
    public bool PasswordChangeRequired { get; set; } = false;
    public bool IsDeleted { get; set; } = false;
    public DateTime? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
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
    public int? CreatedBy { get; set; } // FK to Users.Id (migrated from username string to User ID)
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

    // Email Export Configuration
    public bool IsEmailExport { get; set; } = false; // Flag: is this an email export profile?
    public int? EmailTemplateId { get; set; } // FK to QueryTemplates table - Scriban template for email body
    public string? EmailRecipientsColumn { get; set; } // Query column name for TO recipients (extracted from results)
    public string? EmailRecipientsHardcoded { get; set; } // Hardcoded recipient email address
    public bool UseHardcodedRecipients { get; set; } = false; // Flag: use hardcoded recipient instead of column
    public string? EmailCcColumn { get; set; } // Query column name for CC recipients (extracted from results)
    public string? EmailCcHardcoded { get; set; } // Hardcoded CC email address
    public bool UseHardcodedCc { get; set; } = false; // Flag: use hardcoded CC instead of column
    public string? EmailSubjectColumn { get; set; } // Query column name for email subject (extracted from results)
    public string? EmailSubjectHardcoded { get; set; } // Hardcoded email subject
    public bool UseHardcodedSubject { get; set; } = false; // Flag: use hardcoded subject instead of column
    public int EmailSuccessThresholdPercent { get; set; } = 60; // Mark as succeeded if this % or more of emails succeed (prevents retries on partial failures)
    public string? EmailAttachmentConfig { get; set; } // JSON configuration for email attachments (opt-in)

    // Email Approval Workflow Configuration
    public bool EmailApprovalRequired { get; set; } = false; // Enable/disable manual approval for emails
    public string? EmailApprovalRoles { get; set; } // JSON array of roles allowed to approve (e.g., ["Admin", "User"])

    public string? DependsOnProfileIds { get; set; } // Comma-separated profile IDs for dependencies
    public bool IsEnabled { get; set; } = true;
    public required string Hash { get; set; } // SHA256 for tamper detection
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int? CreatedBy { get; set; } // FK to Users.Id (migrated from username string to User ID)
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
    public string? ProfileName { get; set; } // Profile name from JOIN with Profiles table
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

    // Email Approval Workflow Tracking
    public string? ApprovalStatus { get; set; } // null, Pending, Approved, Rejected, Sent (tracks approval status separately)
    public int? PendingEmailApprovalId { get; set; } // FK to PendingEmailApprovals table (if email approval is required)
    public DateTime? ApprovedAt { get; set; } // When email was approved

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
    public required string PerformedBy { get; set; } // Username string at time of action (historical record)
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

// ===== Email Approval Workflow Models =====

/// <summary>
/// Pending email approval entity for manual approval workflow
/// Emails requiring approval are stored here before sending
/// </summary>
public class PendingEmailApproval
{
    public int Id { get; set; }
    public string Guid { get; set; } = System.Guid.NewGuid().ToString(); // Public GUID for API access (prevents enumeration)
    public int ProfileId { get; set; } // FK to Profiles table
    public int ProfileExecutionId { get; set; } // FK to ProfileExecutions table
    public required string Recipients { get; set; } // CSV or JSON serialized recipient emails
    public string? CcAddresses { get; set; } // Optional CC addresses
    public required string Subject { get; set; } // Email subject
    public required string HtmlBody { get; set; } // Email body (rendered HTML)
    public string? AttachmentConfig { get; set; } // JSON serialized attachment metadata/paths
    public string? ReefId { get; set; } // ReefId for delta sync tracking
    public string? DeltaSyncHash { get; set; } // Delta sync hash for this row
    public string Status { get; set; } = "Pending"; // Pending, Approved, Rejected, Sent, Failed
    public int? ApprovedByUserId { get; set; } // FK to Users table - who approved/rejected
    public DateTime? ApprovedAt { get; set; } // When email was approved/rejected
    public string? ApprovalNotes { get; set; } // Notes/reason for approval or rejection
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; } // Auto-cleanup after N days
    public string? ErrorMessage { get; set; } // Error message if sending failed
    public DateTime? SentAt { get; set; } // When email was actually sent
    public required string Hash { get; set; } // SHA256 for integrity checking
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

// ===== Email Attachment Configuration Models =====

/// <summary>
/// Attachment configuration for email export profiles (JSON)
/// </summary>
public class AttachmentConfig
{
    public bool Enabled { get; set; }
    public required string Mode { get; set; } // "Binary" | "DirectPath"
    public BinaryAttachmentMode? Binary { get; set; }
    public DirectPathAttachmentMode? DirectPath { get; set; }
    public string Deduplication { get; set; } = "Auto"; // Auto | ByFilename | ByHash
    public int MaxAttachmentsPerEmail { get; set; } = 50;
    public string MissingFileStrategy { get; set; } = "Skip"; // Skip | Fail
}

/// <summary>
/// Binary attachment mode - files embedded in query results
/// </summary>
public class BinaryAttachmentMode
{
    public required string ContentColumnName { get; set; }
    public required string FilenameColumnName { get; set; }
}

/// <summary>
/// Direct path attachment mode - files from shared drives/disk locations
/// </summary>
public class DirectPathAttachmentMode
{
    public required string BaseDirectory { get; set; }
    public required string FilenameTemplate { get; set; }
}

/// <summary>
/// Validation result for attachment configuration
/// </summary>
public class AttachmentValidationResult
{
    public bool IsValid { get; set; } = true;
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public List<string> Info { get; set; } = new();
}

/// <summary>
/// Email attachment resolved from query result or file system
/// </summary>
public class EmailAttachment
{
    public required string Filename { get; set; }
    public required byte[] Content { get; set; }
    public string ContentType { get; set; } = "application/octet-stream";
}

/// <summary>
/// System notification settings entity
/// Stored in database with hybrid encryption for sensitive config
/// References an EmailDestination for message delivery
/// </summary>
public class NotificationSettings
{
    public int Id { get; set; }
    public bool IsEnabled { get; set; } = false;
    public int DestinationId { get; set; } // FK to Destinations table (Email type)
    public string? DestinationName { get; set; } // Cached destination name for display

    // Trigger Flags
    public bool NotifyOnJobFailure { get; set; } = true;
    public bool NotifyOnJobSuccess { get; set; } = false;
    public bool NotifyOnProfileFailure { get; set; } = true;
    public bool NotifyOnProfileSuccess { get; set; } = false;
    public bool NotifyOnDatabaseSizeThreshold { get; set; } = true;
    public long DatabaseSizeThresholdBytes { get; set; } = 1_073_741_824; // 1 GB default
    public bool NotifyOnNewUser { get; set; } = false;
    public bool NotifyOnNewApiKey { get; set; } = true;
    public bool NotifyOnNewWebhook { get; set; } = false;
    public bool NotifyOnNewEmailApproval { get; set; } = false;
    public int NewEmailApprovalCooldownHours { get; set; } = 24; // Default cooldown: once per 24 hours

    // Instance Exposure Configuration (expose external Reef instance in email templates)
    public bool EnableCTA { get; set; } = false;
    public string? CTAUrl { get; set; } // External Reef instance URL

    // Email Configuration
    public string? RecipientEmails { get; set; } // Comma-separated; can also be overridden in Destination config

    // Metadata
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public string Hash { get; set; } = string.Empty; // SHA256 for tamper detection
}

/// <summary>
/// Request model for testing notification settings
/// </summary>
public class TestNotificationRequest
{
    public int DestinationId { get; set; }
    public string? RecipientEmails { get; set; }
}

/// <summary>
/// Email template for notifications - stored in database for admin customization
/// Allows users to customize email subjects and HTML bodies for each notification type
/// </summary>
public class NotificationEmailTemplate
{
    public int Id { get; set; }

    /// <summary>
    /// Template type: ProfileSuccess, ProfileFailure, JobSuccess, JobFailure, NewUser, NewApiKey, NewWebhook, DatabaseSizeThreshold
    /// </summary>
    public string TemplateType { get; set; } = string.Empty;

    /// <summary>
    /// Email subject line
    /// </summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>
    /// HTML body for email
    /// </summary>
    public string HtmlBody { get; set; } = string.Empty;

    /// <summary>
    /// Whether this is the default template (system-generated)
    /// </summary>
    public bool IsDefault { get; set; } = true;

    /// <summary>
    /// Call-to-action button text for this template (e.g., "View Execution", "Open Dashboard")
    /// </summary>
    public string? CTAButtonText { get; set; }

    /// <summary>
    /// Optional URL override for this template. If null, uses global CTAUrl from NotificationSettings
    /// </summary>
    public string? CTAUrlOverride { get; set; }

    /// <summary>
    /// Metadata
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Username change history for audit trail and reference tracking
/// </summary>
public class UsernameHistory
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public required string OldUsername { get; set; }
    public required string NewUsername { get; set; }
    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;
    public required string ChangedBy { get; set; } // Username of admin who made the change
    public int? ChangedByUserId { get; set; } // FK to Users table
    public string? IpAddress { get; set; }
    public string? Reason { get; set; } // Optional reason for the change
}