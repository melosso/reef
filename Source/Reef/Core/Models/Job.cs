namespace Reef.Core.Models;

public class Job
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public JobType Type { get; set; }
    public int? ProfileId { get; set; } // References either Profiles or ImportProfiles depending on ProfileType
    public string? ProfileType { get; set; } // null or 'export' = export profile; 'import' = import profile
    public int? DestinationId { get; set; } // Optional destination override
    public string? CustomActionJson { get; set; } // For custom job types
    
    // Scheduling
    public ScheduleType ScheduleType { get; set; }
    public string? CronExpression { get; set; }
    public int? IntervalMinutes { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public TimeSpan? StartTime { get; set; }
    public TimeSpan? EndTime { get; set; }
    public string? WeekDays { get; set; } // Comma-separated day numbers (0=Monday, 6=Sunday) for weekly schedules
    public int? MonthDay { get; set; } // Day of month (1-31) for monthly schedules
    
    // Job Configuration
    public int MaxRetries { get; set; } = 3;
    public int TimeoutMinutes { get; set; } = 60;
    public int Priority { get; set; } = 5; // 1-10, 10 = highest
    public bool AllowConcurrent { get; set; } = false;
    public string? DependsOnJobIds { get; set; } // Comma-separated job IDs
    public bool AutoPauseEnabled { get; set; } = true; // Enable auto-pause after 10 failures (opt-out)
    
    // Status
    public bool IsEnabled { get; set; } = true;
    public JobStatus Status { get; set; } = JobStatus.Idle;
    public DateTime? NextRunTime { get; set; }
    public DateTime? LastRunTime { get; set; }
    public DateTime? LastSuccessTime { get; set; }
    public DateTime? LastFailureTime { get; set; }
    public int ConsecutiveFailures { get; set; }
    
    // Metadata
    public string Tags { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ModifiedAt { get; set; }
    public string? CreatedBy { get; set; }
    public string Hash { get; set; } = string.Empty;
}

public enum JobType
{
    ProfileExecution = 1,      // Execute a profile
    DataTransfer = 2,          // Transfer files between destinations
    Cleanup = 3,               // Delete old files
    HealthCheck = 4,           // System health check
    BackupDatabase = 5,        // Backup Reef.db
    CustomScript = 6,          // Execute custom script
    ApiCall = 7,               // Call external API
    EmailReport = 8,           // Send email report
    FileArchive = 9,           // Archive old exports
    DataValidation = 10        // Validate data integrity
}

public enum ScheduleType
{
    Manual = 0,          // No schedule, manual execution only
    Cron = 1,            // Cron expression
    Interval = 2,        // Fixed interval
    Daily = 3,           // Daily at specific time
    Weekly = 4,          // Weekly on specific days
    Monthly = 5,         // Monthly on specific day
    OnDependency = 6,    // Run after dependencies complete
    Webhook = 7,         // Triggered by webhook
    FileWatcher = 8      // Triggered by file system event
}

public enum JobStatus
{
    Idle = 0,            // Not running
    Scheduled = 1,       // Scheduled for execution
    Running = 2,         // Currently executing
    Completed = 3,       // Completed successfully
    Failed = 4,          // Failed
    Cancelled = 5,       // Cancelled by user
    Disabled = 6,        // Job is disabled
    Waiting = 7,         // Waiting for dependencies
    Retrying = 8         // Retrying after failure
}

public class JobExecution
{
    public long Id { get; set; }
    public int JobId { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public JobStatus Status { get; set; }
    public int AttemptNumber { get; set; }
    public string? OutputData { get; set; }
    public string? ErrorMessage { get; set; }
    public string? StackTrace { get; set; }
    public long? BytesProcessed { get; set; }
    public int? RowsProcessed { get; set; }
    public string? TriggeredBy { get; set; }
    public string? ServerNode { get; set; }
    public string? ExecutionContext { get; set; } // JSON
}

public class JobSchedule
{
    public int JobId { get; set; }
    public DateTime ScheduledFor { get; set; }
    public bool IsRecurring { get; set; }
    public string? RecurrenceRule { get; set; }
}

public class JobDependency
{
    public int JobId { get; set; }
    public int DependsOnJobId { get; set; }
    public DependencyType Type { get; set; }
    public string? Condition { get; set; }
}

public enum DependencyType
{
    MustSucceed = 1,     // Parent must complete successfully
    MustComplete = 2,    // Parent must complete (success or failure)
    MustFail = 3         // Parent must fail (negative dependency)
}

public class JobTriggerRequest
{
    public int JobId { get; set; }
    public bool IgnoreDependencies { get; set; }
    public Dictionary<string, object>? Parameters { get; set; }
}

public class JobControlRequest
{
    public int JobId { get; set; }
    public JobControlAction Action { get; set; }
    public string? Reason { get; set; }
}

public enum JobControlAction
{
    Start,
    Stop,
    Pause,
    Resume,
    Restart,
    Cancel
}