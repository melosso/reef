using Reef.Core.Models;

namespace Reef.Legacy.Models;

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
