using Reef.Core.Models;

namespace Reef.Legacy.Models;

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
