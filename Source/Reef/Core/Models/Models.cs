using Reef.Legacy.Models;

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
    public string? Email { get; set; }
    public bool MfaEnabled { get; set; } = false;
    public string? MfaMethod { get; set; } // "totp" or "email"
    public string? TotpSecret { get; set; } // base32-encoded TOTP secret
    public string? PendingTotpSecret { get; set; } // in-flight during setup, before confirmation
    public string? BackupCodes { get; set; } // JSON array of SHA-256 hashed backup codes
    public string OidcSubject { get; set; } = ""; // subject claim from the configured SSO provider, empty when unlinked
}

/// <summary>
/// API key entity for programmatic access
/// </summary>
public class ApiKey
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? KeyPrefix { get; set; }
    public required string KeyHash { get; set; }
    public required string Permissions { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public bool IsActive { get; set; } = true;
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
/// Webhook trigger entity for external integrations
/// </summary>
public class WebhookTrigger
{
    public int Id { get; set; }
    public int? ProfileId { get; set; } // Nullable to support Job webhooks
    public int? JobId { get; set; } // Nullable to support Profile webhooks
    public int? ImportProfileId { get; set; } // Nullable to support Import Profile webhooks
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
/// Configuration for pre-processing or post-processing execution
/// Stored as JSON in Profile.PreProcessConfig or Profile.PostProcessConfig
/// </summary>
public class ProcessingConfig
{
    /// <summary>
    /// Type of processing: Query, StoredProcedure, or Script
    /// </summary>
    public required string Type { get; set; } // Query, StoredProcedure, Script

    /// <summary>
    /// SQL command to execute (can contain {placeholder} variables)
    /// For Query: SELECT, UPDATE, DELETE, INSERT, etc.
    /// For StoredProcedure: Procedure name (without EXEC/CALL)
    /// Not used when Type is Script.
    /// </summary>
    public string? Command { get; set; }

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

    /// <summary>
    /// Script interpreter to use when Type is Script: pwsh, python, node, bash, sh, cmd
    /// </summary>
    public string? Interpreter { get; set; }

    /// <summary>
    /// When Type is Script: either the inline script source or a path to a script
    /// file, depending on <see cref="ScriptIsInline"/>.
    /// </summary>
    public string? ScriptPathOrInline { get; set; }

    /// <summary>
    /// When Type is Script: whether ScriptPathOrInline holds inline source (true)
    /// or a file path (false).
    /// </summary>
    public bool ScriptIsInline { get; set; }

    /// <summary>
    /// When Type is Script: names of environment variables to pass through to
    /// the script process. Everything else is withheld to avoid leaking secrets.
    /// </summary>
    public List<string>? EnvAllowlist { get; set; }
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

    /// <summary>
    /// Webhook/job trigger parameters passed into ExecuteProfileAsync.
    /// Forwarded to Script-type processing steps as part of the stdin JSON
    /// context, so a post-process webhook script can see what triggered the run.
    /// </summary>
    public Dictionary<string, string>? WebhookParameters { get; set; }
}

/// <summary>
/// Pending email approval entity for manual approval workflow
/// Emails requiring approval are stored here before sending
/// </summary>
public class PendingEmailApproval
{
    public int Id { get; set; }
    public string Guid { get; set; } = System.Guid.NewGuid().ToString(); // Public GUID for API access (prevents enumeration)
    public int? ProfileId { get; set; }
    public int? ProfileExecutionId { get; set; }
    public int? WorkflowExecutionId { get; set; }
    public string? NodeId { get; set; }
    public int? DestinationId { get; set; }
    public string? ApproverRoles { get; set; }
    public string? WorkflowName { get; set; }
    public required string Recipients { get; set; } // CSV or JSON serialized recipient emails
    public string? CcAddresses { get; set; } // Optional CC addresses
    public required string Subject { get; set; } // Email subject
    public required string HtmlBody { get; set; } // Email body (rendered HTML)
    public string? AttachmentConfig { get; set; } // JSON serialized attachment metadata/paths
    public string? ReefId { get; set; } // ReefId for delta sync tracking
    public string? DeltaSyncHash { get; set; } // Delta sync hash for this row
    public string? DeltaSyncRowType { get; set; } // "New", "Changed", or "Deleted" - for metrics tracking
    public string Status { get; set; } = "Pending"; // Pending, Approved, Rejected, Skipped, Sent, Failed
    public int? ApprovedByUserId { get; set; } // FK to Users table - who approved/rejected
    public DateTime? ApprovedAt { get; set; } // When email was approved/rejected
    public int? SkippedByUserId { get; set; } // FK to Users table - who skipped
    public DateTime? SkippedAt { get; set; } // When email was skipped
    public string? ApprovalNotes { get; set; } // Notes/reason for approval, rejection, or skip
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; } // Auto-cleanup after N days
    public string? ErrorMessage { get; set; } // Error message if sending failed
    public DateTime? SentAt { get; set; } // When email was actually sent
    public required string Hash { get; set; } // SHA256 for integrity checking
}

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
    public bool ResetOnSchemaChange { get; set; }

    public static DeltaSyncConfig FromProfile(Profile profile) => new()
    {
        Enabled = profile.DeltaSyncEnabled,
        ReefIdColumn = profile.DeltaSyncReefIdColumn!,
        HashAlgorithm = profile.DeltaSyncHashAlgorithm ?? "SHA256",
        IncludeDeleted = profile.DeltaSyncTrackDeletes,
        RetentionDays = profile.DeltaSyncRetentionDays,
        DuplicateStrategy = profile.DeltaSyncDuplicateStrategy ?? "Strict",
        NullStrategy = profile.DeltaSyncNullStrategy ?? "Strict",
        NumericPrecision = profile.DeltaSyncNumericPrecision ?? 6,
        RemoveNonPrintable = profile.DeltaSyncRemoveNonPrintable,
        ReefIdNormalization = profile.DeltaSyncReefIdNormalization ?? "Trim",
        ResetOnSchemaChange = profile.DeltaSyncResetOnSchemaChange
    };
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
/// Database configuration - used by all services
/// </summary>
public class DatabaseConfig
{
    public required string ConnectionString { get; init; }
}

/// <summary>
/// Attachment configuration for email export profiles (JSON)
/// </summary>
public class AttachmentConfig
{
    public bool Enabled { get; set; }
    public required string Mode { get; set; } // "Binary" | "DirectPath" | "DocumentTemplate"
    public BinaryAttachmentMode? Binary { get; set; }
    public DirectPathAttachmentMode? DirectPath { get; set; }
    public DocumentTemplateAttachmentMode? DocumentTemplate { get; set; }
    public string Deduplication { get; set; } = "Auto"; // Auto | ByFilename | ByHash
    public string Failsafe { get; set; } = "Disabled"; // Disabled | RequireAttachments
    public int MaxAttachmentsPerEmail { get; set; } = 50;
    public string MissingFileStrategy { get; set; } = "Skip"; // Skip | Fail

    /// <summary>
    /// Pre-generated attachments for approval flow (documents generated before approval, stored as base64)
    /// </summary>
    public List<PreGeneratedAttachment>? GeneratedAttachments { get; set; }

    public static AttachmentConfig? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<AttachmentConfig>(json);
        }
        catch (System.Text.Json.JsonException ex)
        {
            throw new InvalidOperationException($"Attachment configuration is invalid: {ex.Message}", ex);
        }
    }
}

/// <summary>
/// Pre-generated attachment stored with approval (contains base64-encoded document)
/// </summary>
public class PreGeneratedAttachment
{
    public required string Filename { get; set; }
    public required string ContentType { get; set; }
    public required string ContentBase64 { get; set; }
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
/// Document template attachment mode - generate PDFs/DOCX from template for each row
/// </summary>
public class DocumentTemplateAttachmentMode
{
    public int TemplateId { get; set; }
    public string? FilenameColumnName { get; set; }
    public string? PageSize { get; set; }
    public string? Orientation { get; set; }
    public string? Watermark { get; set; }
    public bool GeneratePerRow { get; set; } = false; // If true, generate one document per row (for invoice approval scenarios)
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
    public bool NotifyOnImportProfileFailure { get; set; } = true;
    public bool NotifyOnImportProfileSuccess { get; set; } = false;
    public bool NotifyOnDatabaseSizeThreshold { get; set; } = true;
    public long DatabaseSizeThresholdBytes { get; set; } = 1_073_741_824; // 1 GB default
    public bool NotifyOnNewUser { get; set; } = false;
    public bool NotifyOnNewApiKey { get; set; } = true;
    public bool NotifyOnNewWebhook { get; set; } = false;
    public bool NotifyOnNewEmailApproval { get; set; } = false;
    public int NewEmailApprovalCooldownHours { get; set; } = 24; // Default cooldown: once per 24 hours
    public DateTime? NewEmailApprovalCooldownTimestamp { get; set; } // Last time email approval notification was sent

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
/// Single-provider generic OIDC SSO configuration (works with Pocket ID, Azure Entra ID,
/// or any standard discovery-compliant provider). One row max.
/// </summary>
public class OidcSettings
{
    public int Id { get; set; }
    public bool IsEnabled { get; set; } = false;
    public string Name { get; set; } = "Single Sign-On"; // shown on the login button, e.g. "Continue with {Name}"
    public string Authority { get; set; } = ""; // issuer base URL; discovery doc is read from {Authority}/.well-known/openid-configuration
    public string ClientId { get; set; } = "";
    public string? ClientSecretEncrypted { get; set; }
    public string Scopes { get; set; } = "openid profile email";
    public string UsernameClaim { get; set; } = "preferred_username";
    public string EmailClaim { get; set; } = "email";
    public bool CreateAccounts { get; set; } = false; // provision a new User on first sign-in when no match is found
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Admin request body for saving OIDC SSO settings. ClientSecret is plaintext and optional;
/// omit it (null) to keep whatever secret is already stored.
/// </summary>
public class OidcSettingsRequest
{
    public bool IsEnabled { get; set; }
    public string Name { get; set; } = "Single Sign-On";
    public string Authority { get; set; } = "";
    public string ClientId { get; set; } = "";
    public string? ClientSecret { get; set; }
    public string Scopes { get; set; } = "openid profile email";
    public string UsernameClaim { get; set; } = "preferred_username";
    public string EmailClaim { get; set; } = "email";
    public bool CreateAccounts { get; set; }
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