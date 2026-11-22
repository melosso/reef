using Reef.Core.Models;

namespace Reef.Legacy.Models;

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
    public int? OutputDestinationEndpointId { get; set; } // FK to DestinationEndpoints table (optional)
    public int? TemplateId { get; set; } // FK to QueryTemplates table (optional - for custom transformations)
    public string? TransformationOptionsJson { get; set; } // JSON options for SQL Server native transformations (ForJsonOptions, ForXmlOptions)

    // Pre-Processing Configuration
    public string? PreProcessType { get; set; } // null, Query, StoredProcedure
    public string? PreProcessConfig { get; set; } // JSON configuration (ProcessingConfig)
    public bool PreProcessRollbackOnFailure { get; set; } = true; // Rollback pre-processing if main query fails

    // Post-Processing Configuration (enhanced existing)
    public string? PostProcessType { get; set; } // null, Query, StoredProcedure, Webhook
    public string? PostProcessConfig { get; set; } // JSON configuration (ProcessingConfig)
    public string? CanvasLayoutJson { get; set; } // node positions and connections for the canvas editor
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
    public bool EmailGroupBySplitKey { get; set; } = false; // For email exports: group rows by split key (one email per split key value)

    // Unique human-readable short code, e.g. "P-A3F1" (set on creation, immutable)
    public string Code { get; set; } = "";
}
