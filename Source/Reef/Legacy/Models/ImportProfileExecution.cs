using Reef.Core.Models;

namespace Reef.Legacy.Models;

/// <summary>
/// Execution record for an import profile run, mirrors ImportProfileExecutions table schema.
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

    // Pre/post-process Script step observability (Type=Script only)
    public string? PreProcessStdout { get; set; }
    public string? PreProcessStderr { get; set; }
    public int? PreProcessExitCode { get; set; }
    public string? PostProcessStdout { get; set; }
    public string? PostProcessStderr { get; set; }
    public int? PostProcessExitCode { get; set; }
    public int FilesProcessed { get; set; }
    public long BytesProcessed { get; set; }

    // Delta sync counters
    public int DeltaSyncNewRows { get; set; }
    public int DeltaSyncChangedRows { get; set; }
    public int DeltaSyncUnchangedRows { get; set; }
    public int DeltaSyncDeletedRows { get; set; }

    /// <summary>JSON object keyed by phase name, value = elapsed ms</summary>
    public string? PhaseTimingsJson { get; set; }

    /// <summary>JSON array of ImportOutputTargetResult, results from fan-out output targets</summary>
    public string? OutputTargetsJson { get; set; }
}
