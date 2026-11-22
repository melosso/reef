using Reef.Core.Models;

namespace Reef.Legacy.Models;

/// <summary>
/// Individual row-level write error stored for audit/review, mirrors ImportExecutionErrors table.
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
