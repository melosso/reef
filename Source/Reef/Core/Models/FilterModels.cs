namespace Reef.Core.Models;

public class FilterState
{
    public string SearchTerm { get; set; } = "";
    public string Status { get; set; } = "All";
    public string Type { get; set; } = "";
}

public class FilterOption
{
    public string Value { get; set; } = "";
    public string Label { get; set; } = "";
}

public enum ConnectionType
{
    SqlServer,
    MySQL,
    PostgreSQL
}

public class JobStatistics
{
    public int TotalJobs { get; set; }
    public int RunningJobs { get; set; }
    public int QueuedJobs { get; set; }
    public int IdleJobs { get; set; }
    public int FailedJobs { get; set; }
    public int AutoPausedJobs { get; set; }
}

public class QueueMetrics
{
    public int PendingTasks { get; set; }
    public int RunningTasks { get; set; }
    public int CompletedToday { get; set; }
    public double AverageExecutionTimeMs { get; set; }
}

public class ApprovalStatistics
{
    public int PendingCount { get; set; }
    public int QueuedCount { get; set; }
    public int SentCount { get; set; }
    public int SkippedCount { get; set; }
    public int RejectedCount { get; set; }
}

public class SystemInfo
{
    public string Version { get; set; } = "";
    public long DatabaseSizeBytes { get; set; }
    public int TotalExecutions { get; set; }
    public TimeSpan Uptime { get; set; }
}
