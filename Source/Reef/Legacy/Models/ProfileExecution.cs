using Reef.Core.Models;

namespace Reef.Legacy.Models;

/// <summary>
/// Profile execution history entity
/// </summary>
public class ProfileExecution
{
    public int Id { get; set; }
    public int ProfileId { get; set; }
    public int? JobId { get; set; } // Optional: Which job triggered this execution
    public string? ProfileName { get; set; } // Profile name from JOIN with Profiles table
    public string? ProfileCode { get; set; } // Profile code from JOIN with Profiles table
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
    public string? PreProcessStdout { get; set; } // Script type only
    public string? PreProcessStderr { get; set; } // Script type only
    public int? PreProcessExitCode { get; set; } // Script type only

    // Post-Processing Phase Tracking
    public DateTime? PostProcessStartedAt { get; set; }
    public DateTime? PostProcessCompletedAt { get; set; }
    public string? PostProcessStatus { get; set; } // null, Success, Failed, Skipped
    public string? PostProcessError { get; set; }
    public long? PostProcessTimeMs { get; set; }
    public string? PostProcessStdout { get; set; } // Script type only
    public string? PostProcessStderr { get; set; } // Script type only
    public int? PostProcessExitCode { get; set; } // Script type only

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
