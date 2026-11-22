using Reef.Core.Models;

namespace Reef.Legacy.Models;

/// <summary>
/// Result from writing rows to a single fan-out output target.
/// Serialised into ImportProfileExecution.OutputTargetsJson.
/// </summary>
public record ImportOutputTargetResult(
    int DestinationId,
    string DestinationName,
    string Status,        // Success | Failed | Skipped
    string? FinalPath,
    string? ErrorMessage,
    int RowsWritten);
