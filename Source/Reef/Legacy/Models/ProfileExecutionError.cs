using Reef.Core.Models;

namespace Reef.Legacy.Models;

/// <summary>
/// Per-send failure record for email export or other split-based executions
/// </summary>
public class ProfileExecutionError
{
    public int Id { get; set; }
    public int ExecutionId { get; set; }
    public string ErrorType { get; set; } = "Unknown";
    public string? Phase { get; set; }
    public string? Detail { get; set; }  // e.g. recipient address
    public string ErrorMessage { get; set; } = "";
    public DateTime OccurredAt { get; set; }
}
