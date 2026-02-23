using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using NCrontab;
using Reef.Core.Models;
using Serilog;
using System.Collections.Concurrent;

namespace Reef.Core.Services;

/// <summary>
/// Production-ready job service with comprehensive edge case handling
/// </summary>
public class JobService
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _updateLock = new(1, 1);
    private readonly ConcurrentDictionary<int, SemaphoreSlim> _jobLocks = new();

    // Circuit breaker configuration
    private readonly int _circuitBreakerThreshold;
    private readonly bool _autoResumeAfterPause;
    private readonly int _autoResumeCooldownHours;

    public JobService(DatabaseConfig config, IConfiguration configuration)
    {
        _connectionString = config.ConnectionString;

        // Read circuit breaker configuration with defaults
        _circuitBreakerThreshold = configuration.GetValue<int>("Reef:Jobs:CircuitBreakerThreshold", 10);
        _autoResumeAfterPause = configuration.GetValue<bool>("Reef:Jobs:AutoResumeAfterPause", false);
        _autoResumeCooldownHours = configuration.GetValue<int>("Reef:Jobs:AutoResumeCooldownHours", 1);

        // Validate configuration
        if (_circuitBreakerThreshold < 1 || _circuitBreakerThreshold > 100)
        {
            Log.Warning("Reef:Jobs:CircuitBreakerThreshold ({Value}) out of range (1-100), using default: 10", _circuitBreakerThreshold);
            _circuitBreakerThreshold = 10;
        }

        if (_autoResumeCooldownHours < 1 || _autoResumeCooldownHours > 168)
        {
            Log.Warning("Reef:Jobs:AutoResumeCooldownHours ({Value}) out of range (1-168 hours), using default: 1", _autoResumeCooldownHours);
            _autoResumeCooldownHours = 1;
        }

        Log.Debug("JobService initialized - Circuit Breaker: {Threshold} failures, Auto-Resume: {AutoResume} (cooldown: {Cooldown}h)",
            _circuitBreakerThreshold, _autoResumeAfterPause, _autoResumeCooldownHours);
    }

    #region CRUD Operations

    private static DateTime? EnsureUtc(DateTime? dateTime)
    {
        if (!dateTime.HasValue) return null;
        
        if (dateTime.Value.Kind == DateTimeKind.Unspecified)
        {
            return DateTime.SpecifyKind(dateTime.Value, DateTimeKind.Utc);
        }
        
        return dateTime.Value.Kind == DateTimeKind.Utc 
            ? dateTime.Value 
            : dateTime.Value.ToUniversalTime();
    }

    private static void NormalizeJobDateTimes(Job job)
    {
        if (job == null) return;
        
        job.NextRunTime = EnsureUtc(job.NextRunTime);
        job.LastRunTime = EnsureUtc(job.LastRunTime);
        job.LastSuccessTime = EnsureUtc(job.LastSuccessTime);
        job.LastFailureTime = EnsureUtc(job.LastFailureTime);
        job.StartDate = EnsureUtc(job.StartDate);
        job.EndDate = EnsureUtc(job.EndDate);
        job.CreatedAt = EnsureUtc(job.CreatedAt) ?? DateTime.UtcNow;
        job.ModifiedAt = EnsureUtc(job.ModifiedAt);
    }

    public async Task<Job?> GetByIdAsync(int id)
    {
        using var conn = new SqliteConnection(_connectionString);
        var job = await conn.QuerySingleOrDefaultAsync<Job>(
            "SELECT * FROM Jobs WHERE Id = @Id", new { Id = id });
        
        if (job != null)
        {
            NormalizeJobDateTimes(job);
        }
        
        return job;
    }

    public async Task<IEnumerable<Job>> GetAllAsync()
    {
        using var conn = new SqliteConnection(_connectionString);
        var jobs = (await conn.QueryAsync<Job>("SELECT * FROM Jobs ORDER BY Name")).ToList();
        
        foreach (var job in jobs)
        {
            NormalizeJobDateTimes(job);
        }
        
        return jobs;
    }

    public async Task<IEnumerable<Job>> GetAllAsync(bool enabledOnly)
    {
        using var conn = new SqliteConnection(_connectionString);
        
        IEnumerable<Job> jobs;
        if (enabledOnly)
        {
            jobs = (await conn.QueryAsync<Job>(
                "SELECT * FROM Jobs WHERE IsEnabled = 1 ORDER BY Name")).ToList();
        }
        else
        {
            return await GetAllAsync();
        }
        
        foreach (var job in jobs)
        {
            NormalizeJobDateTimes(job);
        }
        
        return jobs;
    }

    public async Task<IEnumerable<Job>> GetByProfileIdAsync(int profileId)
    {
        using var conn = new SqliteConnection(_connectionString);
        var jobs = (await conn.QueryAsync<Job>(
            "SELECT * FROM Jobs WHERE ProfileId = @ProfileId",
            new { ProfileId = profileId })).ToList();

        foreach (var job in jobs)
        {
            NormalizeJobDateTimes(job);
        }

        return jobs;
    }

    public async Task<IEnumerable<Job>> GetByImportProfileIdAsync(int importProfileId)
    {
        using var conn = new SqliteConnection(_connectionString);
        var jobs = (await conn.QueryAsync<Job>(
            "SELECT * FROM Jobs WHERE ImportProfileId = @ImportProfileId",
            new { ImportProfileId = importProfileId })).ToList();

        foreach (var job in jobs)
        {
            NormalizeJobDateTimes(job);
        }

        return jobs;
    }

    /// <summary>
    /// Validates job configuration to prevent invalid combinations of ProfileId and DestinationId
    /// </summary>
    private async Task ValidateJobConfiguration(Job job)
    {
        // If ProfileId is set (export profile), check if it's an Email export profile
        if (job.ProfileId.HasValue && !job.ImportProfileId.HasValue)
        {
            using var conn = new SqliteConnection(_connectionString);
            const string sql = "SELECT IsEmailExport FROM Profiles WHERE Id = @Id";

            var isEmailExport = await conn.QueryFirstOrDefaultAsync<bool?>(sql, new { Id = job.ProfileId.Value });

            // If profile exists and is an email export profile, DestinationId must be null
            if (isEmailExport == true && job.DestinationId.HasValue)
            {
                throw new InvalidOperationException(
                    "Cannot set a Destination for Email export profiles. Email profiles handle delivery internally.");
            }
        }
    }

    public async Task<Job> CreateAsync(Job job)
    {
        // Validate job configuration before creating
        await ValidateJobConfiguration(job);

        using var conn = new SqliteConnection(_connectionString);

        job.Hash = Reef.Helpers.HashHelper.ComputeDestinationHash(
            job.Name,
            job.Type.ToString(),
            job.ScheduleType.ToString());

        job.CreatedAt = DateTime.UtcNow;
        job.Status = JobStatus.Idle;
        
        // Calculate initial NextRunTime before inserting
        job.NextRunTime = CalculateNextRunTime(job, DateTime.UtcNow);
        
        // Validate NextRunTime is not in the past
        if (job.NextRunTime.HasValue && job.NextRunTime.Value < DateTime.UtcNow)
        {
            Log.Warning("Job {JobName} initial NextRunTime {NextRun} is in the past, recalculating", 
                job.Name, job.NextRunTime);
            job.NextRunTime = CalculateNextRunTime(job, DateTime.UtcNow);
        }
        
        var sql = @"
            INSERT INTO Jobs (
                Name, Description, Type, ProfileId, ImportProfileId, DestinationId, CustomActionJson,
                ScheduleType, CronExpression, IntervalMinutes, StartDate, EndDate, StartTime, EndTime, WeekDays, MonthDay,
                MaxRetries, TimeoutMinutes, Priority, AllowConcurrent, DependsOnJobIds, AutoPauseEnabled,
                IsEnabled, Status, NextRunTime, Tags, CreatedAt, CreatedBy, Hash
            )
            VALUES (
                @Name, @Description, @Type, @ProfileId, @ImportProfileId, @DestinationId, @CustomActionJson,
                @ScheduleType, @CronExpression, @IntervalMinutes, @StartDate, @EndDate, @StartTime, @EndTime, @WeekDays, @MonthDay,
                @MaxRetries, @TimeoutMinutes, @Priority, @AllowConcurrent, @DependsOnJobIds, @AutoPauseEnabled,
                @IsEnabled, @Status, @NextRunTime, @Tags, @CreatedAt, @CreatedBy, @Hash
            );
            SELECT last_insert_rowid();";
        
        job.Id = await conn.ExecuteScalarAsync<int>(sql, job);
        
        Log.Information("Created job {JobId} ({JobName}) with NextRunTime: {NextRun}", 
            job.Id, job.Name, job.NextRunTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "null");
        
        return job;
    }

    public async Task<bool> UpdateAsync(Job job)
    {
        var jobLock = _jobLocks.GetOrAdd(job.Id, _ => new SemaphoreSlim(1, 1));
        await jobLock.WaitAsync();
        try
        {
            // Validate job configuration before updating
            await ValidateJobConfiguration(job);

            using var conn = new SqliteConnection(_connectionString);

            job.Hash = Reef.Helpers.HashHelper.ComputeDestinationHash(
                job.Name,
                job.Type.ToString(),
                job.ScheduleType.ToString());

            job.ModifiedAt = DateTime.UtcNow;
            
            // Recalculate next run time with current UTC time
            var now = DateTime.UtcNow;
            job.NextRunTime = CalculateNextRunTime(job, now);
            
            // Validate NextRunTime is not in the past
            if (job.NextRunTime.HasValue && job.NextRunTime.Value < now)
            {
                Log.Warning("Job {JobId} ({JobName}) NextRunTime {NextRun} is in the past after update, forcing recalculation", 
                    job.Id, job.Name, job.NextRunTime);
                job.NextRunTime = CalculateNextRunTime(job, now);
            }
            
            var sql = @"
                UPDATE Jobs
                SET Name = @Name, Description = @Description, Type = @Type,
                    ProfileId = @ProfileId, ImportProfileId = @ImportProfileId, DestinationId = @DestinationId, CustomActionJson = @CustomActionJson,
                    ScheduleType = @ScheduleType, CronExpression = @CronExpression,
                    IntervalMinutes = @IntervalMinutes, StartDate = @StartDate, EndDate = @EndDate,
                    StartTime = @StartTime, EndTime = @EndTime, WeekDays = @WeekDays, MonthDay = @MonthDay,
                    MaxRetries = @MaxRetries, TimeoutMinutes = @TimeoutMinutes, Priority = @Priority,
                    AllowConcurrent = @AllowConcurrent, DependsOnJobIds = @DependsOnJobIds, AutoPauseEnabled = @AutoPauseEnabled,
                    IsEnabled = @IsEnabled, NextRunTime = @NextRunTime, Tags = @Tags,
                    ModifiedAt = @ModifiedAt, Hash = @Hash
                WHERE Id = @Id";
            
            var rows = await conn.ExecuteAsync(sql, job);
            
            Log.Information("Updated job {JobId} ({JobName}) - NextRunTime: {NextRun}", 
                job.Id, job.Name, job.NextRunTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "null");
            
            return rows > 0;
        }
        finally
        {
            jobLock.Release();
        }
    }

    public async Task<bool> DeleteAsync(int id)
    {
        using var conn = new SqliteConnection(_connectionString);
        
        // Delete related executions
        await conn.ExecuteAsync("DELETE FROM JobExecutions WHERE JobId = @Id", new { Id = id });
        
        // Delete job
        var sql = "DELETE FROM Jobs WHERE Id = @Id";
        var rows = await conn.ExecuteAsync(sql, new { Id = id });
        
        // Cleanup lock
        _jobLocks.TryRemove(id, out _);
        
        return rows > 0;
    }

    #endregion

    #region Status Management

    public async Task<bool> UpdateStatusAsync(int jobId, JobStatus status)
    {
        using var conn = new SqliteConnection(_connectionString);
        
        var sql = @"
            UPDATE Jobs 
            SET Status = @Status, ModifiedAt = @ModifiedAt
            WHERE Id = @JobId";
        
        var rows = await conn.ExecuteAsync(sql, new 
        { 
            JobId = jobId, 
            Status = status, 
            ModifiedAt = DateTime.UtcNow 
        });
        
        Log.Debug("Updated job {JobId} status to {Status}", jobId, status);
        
        return rows > 0;
    }

    public async Task<bool> UpdateNextRunTimeAsync(int jobId, DateTime? nextRunTime)
    {
        // Validate nextRunTime is not in the past
        if (nextRunTime.HasValue && nextRunTime.Value < DateTime.UtcNow)
        {
            Log.Warning("Attempted to set NextRunTime to past value {NextRun} for job {JobId}, rejecting", 
                nextRunTime, jobId);
            
            // Get the job and recalculate properly
            var job = await GetByIdAsync(jobId);
            if (job != null)
            {
                nextRunTime = CalculateNextRunTime(job, DateTime.UtcNow);
            }
        }
        
        using var conn = new SqliteConnection(_connectionString);
        
        var sql = @"
            UPDATE Jobs 
            SET NextRunTime = @NextRunTime, ModifiedAt = @ModifiedAt
            WHERE Id = @JobId";
        
        var rows = await conn.ExecuteAsync(sql, new 
        { 
            JobId = jobId, 
            NextRunTime = nextRunTime, 
            ModifiedAt = DateTime.UtcNow 
        });
        
        Log.Debug("Updated job {JobId} NextRunTime to {NextRun}", 
            jobId, nextRunTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "null");
        
        return rows > 0;
    }

    #endregion

    #region Execution Management

    public async Task<IEnumerable<Job>> GetDueJobsAsync()
    {
        using var conn = new SqliteConnection(_connectionString);
        
        var now = DateTime.UtcNow;
        
        var sql = @"
            SELECT * FROM Jobs 
            WHERE IsEnabled = 1 
            AND Status IN (@Idle, @Scheduled, @Failed)
            AND NextRunTime IS NOT NULL 
            AND NextRunTime <= @Now
            AND (EndDate IS NULL OR EndDate >= @Now)
            ORDER BY Priority DESC, NextRunTime";
        
        var jobs = (await conn.QueryAsync<Job>(sql, new 
        { 
            Idle = JobStatus.Idle,
            Scheduled = JobStatus.Scheduled,
            Failed = JobStatus.Failed,
            Now = now
        })).ToList();
        
        foreach (var job in jobs)
        {
            NormalizeJobDateTimes(job);
        }
        
        var validJobs = jobs.Where(j => 
            j.NextRunTime.HasValue && 
            j.NextRunTime.Value > now.AddDays(-7)).ToList();
        
        if (validJobs.Count < jobs.Count)
        {
            var invalidCount = jobs.Count - validJobs.Count;
            Log.Warning("Filtered out {Count} jobs with NextRunTime >7 days in the past", invalidCount);
            
            foreach (var invalidJob in jobs.Except(validJobs))
            {
                await RecalculateAndUpdateNextRunTimeAsync(invalidJob.Id);
            }
        }
        
        var stuckJobsSql = @"
            SELECT * FROM Jobs 
            WHERE IsEnabled = 1 
            AND NextRunTime IS NULL 
            AND ScheduleType != @Manual";
        
        var stuckJobs = (await conn.QueryAsync<Job>(stuckJobsSql, new 
        { 
            Manual = ScheduleType.Manual 
        })).ToList();
        
        if (stuckJobs.Any())
        {
            Log.Warning("Found {Count} enabled jobs with NULL NextRunTime, auto-fixing", stuckJobs.Count);
            
            foreach (var stuckJob in stuckJobs)
            {
                NormalizeJobDateTimes(stuckJob);
                await RecalculateAndUpdateNextRunTimeAsync(stuckJob.Id);
                
                if (stuckJob.Status == JobStatus.Failed)
                {
                    await UpdateStatusAsync(stuckJob.Id, JobStatus.Idle);
                    await conn.ExecuteAsync(
                        "UPDATE Jobs SET ConsecutiveFailures = 0 WHERE Id = @Id", 
                        new { Id = stuckJob.Id });
                }
            }
        }
        
        return validJobs;
    }
    public async Task<long> CreateExecutionAsync(JobExecution execution)
    {
        using var conn = new SqliteConnection(_connectionString);
        
        execution.StartedAt = DateTime.UtcNow;
        
        var sql = @"
            INSERT INTO JobExecutions (
                JobId, StartedAt, Status, AttemptNumber, TriggeredBy, ServerNode, ExecutionContext
            )
            VALUES (
                @JobId, @StartedAt, @Status, @AttemptNumber, @TriggeredBy, @ServerNode, @ExecutionContext
            );
            SELECT last_insert_rowid();";
        
        var executionId = await conn.ExecuteScalarAsync<long>(sql, execution);
        
        Log.Debug("Created execution {ExecutionId} for job {JobId}", executionId, execution.JobId);
        
        return executionId;
    }

    public async Task<bool> UpdateExecutionAsync(JobExecution execution)
    {
        var jobLock = _jobLocks.GetOrAdd(execution.JobId, _ => new SemaphoreSlim(1, 1));
        await jobLock.WaitAsync();
        try
        {
            using var conn = new SqliteConnection(_connectionString);
            
            execution.CompletedAt = DateTime.UtcNow;
            
            var sql = @"
                UPDATE JobExecutions 
                SET CompletedAt = @CompletedAt, Status = @Status,
                    OutputData = @OutputData, ErrorMessage = @ErrorMessage,
                    StackTrace = @StackTrace, BytesProcessed = @BytesProcessed,
                    RowsProcessed = @RowsProcessed
                WHERE Id = @Id";
            
            var rows = await conn.ExecuteAsync(sql, execution);
            
            // Update job statistics AND recalculate NextRunTime
            if (execution.Status == JobStatus.Completed)
            {
                await UpdateJobSuccessAsync(execution.JobId);
            }
            else if (execution.Status == JobStatus.Failed)
            {
                await UpdateJobFailureAsync(execution.JobId);
            }
            
            Log.Debug("Updated execution {ExecutionId} for job {JobId} - Status: {Status}", 
                execution.Id, execution.JobId, execution.Status);
            
            return rows > 0;
        }
        finally
        {
            jobLock.Release();
        }
    }

    /// <summary>
    /// Update job after successful execution AND recalculate NextRunTime
    /// </summary>
    private async Task UpdateJobSuccessAsync(int jobId)
    {
        using var conn = new SqliteConnection(_connectionString);

        var now = DateTime.UtcNow;

        var job = await conn.QuerySingleOrDefaultAsync<Job>(
            "SELECT * FROM Jobs WHERE Id = @Id", new { Id = jobId });

        if (job == null)
        {
            Log.Warning("Job {JobId} not found during UpdateJobSuccessAsync", jobId);
            return;
        }

        NormalizeJobDateTimes(job);

        // Only calculate NextRunTime for non-Manual jobs
        DateTime? nextRunTime = null;
        if (job.ScheduleType != ScheduleType.Manual)
        {
            nextRunTime = CalculateNextRunTime(job, now);

            if (!nextRunTime.HasValue)
            {
                Log.Error("CalculateNextRunTime returned NULL for job {JobId}, forcing 1-hour retry", jobId);
                nextRunTime = now.AddHours(1);
            }
            else if (nextRunTime.Value <= now)
            {
                Log.Warning("Calculated NextRunTime {NextRun} is not in the future for job {JobId}, adding safety buffer",
                    nextRunTime, jobId);
                nextRunTime = now.AddMinutes(1);
            }
        }

        var sql = @"
            UPDATE Jobs 
            SET LastRunTime = @Now, 
                LastSuccessTime = @Now,
                NextRunTime = @NextRunTime,
                ConsecutiveFailures = 0,
                Status = @Idle,
                ModifiedAt = @Now
            WHERE Id = @JobId";

        await conn.ExecuteAsync(sql, new
        {
            JobId = jobId,
            Now = now,
            NextRunTime = nextRunTime,
            Idle = JobStatus.Idle
        });

        Log.Debug("Job {JobId} success - LastRunTime: {LastRun}, NextRunTime: {NextRun}",
            jobId,
            now.ToString("yyyy-MM-dd HH:mm:ss"),
            nextRunTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "null");
    }

    /// <summary>
    /// Update job after failed execution AND recalculate NextRunTime.
    /// Implements circuit breaker pattern: disables jobs after excessive failures
    ///
    /// Logic:
    /// - Each job run counts as ONE failure if it fails (even after all retries are exhausted)
    /// - The circuit breaker triggers after 10 consecutive JOB FAILURES, not total retry attempts
    /// - Example: If MaxRetries=3 and a job fails 10 times, that's 10 job runs with up to 3 retries each
    ///   (potentially 30 total attempts), but the circuit breaker counts it as 10 failures
    /// - This design prevents infinite retry loops and gives the job multiple chances before pausing
    /// - When auto-paused, the job is disabled (IsEnabled=0) and requires manual resume
    /// </summary>
    private async Task UpdateJobFailureAsync(int jobId)
    {
        using var conn = new SqliteConnection(_connectionString);

        var now = DateTime.UtcNow;

        var job = await conn.QuerySingleOrDefaultAsync<Job>(
            "SELECT * FROM Jobs WHERE Id = @Id", new { Id = jobId });

        if (job == null)
        {
            Log.Warning("Job {JobId} not found during UpdateJobFailureAsync", jobId);
            return;
        }

        NormalizeJobDateTimes(job);

        // Calculate new failure count
        var newFailureCount = job.ConsecutiveFailures + 1;

        // CIRCUIT BREAKER PATTERN: Check if job should be disabled due to excessive failures
        if (job.AutoPauseEnabled && newFailureCount >= _circuitBreakerThreshold)
        {
            // Circuit breaker tripped: Disable job (temporarily or indefinitely based on config)
            if (_autoResumeAfterPause)
            {
                Log.Error("Circuit breaker activated for job {JobId} ({JobName}) after {Failures} consecutive failures - disabling for {Hours} hour(s) cooldown",
                    jobId, job.Name, newFailureCount, _autoResumeCooldownHours);
            }
            else
            {
                Log.Error("Circuit breaker activated for job {JobId} ({JobName}) after {Failures} consecutive failures - disabling indefinitely (requires manual resume)",
                    jobId, job.Name, newFailureCount);
            }

            // Set cooldown time only if auto-resume is enabled, otherwise NULL (indefinite)
            var cooldownUntil = _autoResumeAfterPause ? now.AddHours(_autoResumeCooldownHours) : (DateTime?)null;

            var disableSql = @"
                UPDATE Jobs
                SET LastRunTime = @Now,
                    LastFailureTime = @Now,
                    NextRunTime = @CooldownUntil,
                    ConsecutiveFailures = @Failures,
                    Status = @Failed,
                    IsEnabled = 0,
                    ModifiedAt = @Now,
                    Tags = CASE
                        WHEN Tags IS NULL OR Tags = '' THEN 'circuit-breaker'
                        WHEN Tags LIKE '%circuit-breaker%' THEN Tags
                        ELSE Tags || ',circuit-breaker'
                    END
                WHERE Id = @JobId";

            await conn.ExecuteAsync(disableSql, new
            {
                JobId = jobId,
                Now = now,
                CooldownUntil = cooldownUntil,
                Failures = newFailureCount,
                Failed = JobStatus.Failed
            });

            if (_autoResumeAfterPause)
            {
                Log.Warning("Job {JobId} ({JobName}) temporarily disabled. Will auto-resume at {CooldownUntil}",
                    jobId, job.Name, cooldownUntil?.ToString("yyyy-MM-dd HH:mm:ss") ?? "N/A");
            }
            else
            {
                Log.Warning("Job {JobId} ({JobName}) disabled indefinitely. Requires manual resume via API or UI.",
                    jobId, job.Name);
            }

            // TODO: Send notification to admin about circuit breaker activation
            // await NotifyAdminAsync($"Job {jobId} ({job.Name}) disabled due to {newFailureCount} consecutive failures");

            return;
        }

        // Normal failure handling with exponential backoff
        var nextRunTime = CalculateNextRunTime(job, now);

        if (!nextRunTime.HasValue)
        {
            Log.Error("CalculateNextRunTime returned NULL for job {JobId}, forcing 5-minute retry", jobId);
            nextRunTime = now.AddMinutes(5);
        }
        else if (nextRunTime.Value <= now)
        {
            Log.Warning("Calculated NextRunTime {NextRun} is not in the future after failure for job {JobId}",
                nextRunTime, jobId);

            var backoffMinutes = Math.Min(60, Math.Pow(2, newFailureCount));
            nextRunTime = now.AddMinutes(backoffMinutes);

            Log.Information("Applied exponential backoff of {Minutes} minutes to job {JobId}",
                backoffMinutes, jobId);
        }

        var sql = @"
            UPDATE Jobs
            SET LastRunTime = @Now,
                LastFailureTime = @Now,
                NextRunTime = @NextRunTime,
                ConsecutiveFailures = @Failures,
                Status = @Failed,
                ModifiedAt = @Now
            WHERE Id = @JobId";

        await conn.ExecuteAsync(sql, new
        {
            JobId = jobId,
            Now = now,
            NextRunTime = nextRunTime,
            Failures = newFailureCount,
            Failed = JobStatus.Failed
        });

        Log.Warning("Job {JobId} failed - LastRunTime: {LastRun}, NextRunTime: {NextRun}, Failures: {Failures}",
            jobId,
            now.ToString("yyyy-MM-dd HH:mm:ss"),
            nextRunTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "null",
            newFailureCount);
    }

    /// <summary>
    /// Re-enable jobs that were disabled by circuit breaker after cooldown period
    /// Call this method periodically from scheduler
    /// NOTE: Only runs if AutoResumeAfterPause is enabled in configuration
    /// </summary>
    public async Task ReEnableCircuitBreakerJobsAsync()
    {
        // Skip if auto-resume is disabled (jobs require manual intervention)
        if (!_autoResumeAfterPause)
        {
            return;
        }

        using var conn = new SqliteConnection(_connectionString);

        var now = DateTime.UtcNow;

        var sql = @"
            SELECT * FROM Jobs
            WHERE IsEnabled = 0
            AND Tags LIKE '%circuit-breaker%'
            AND NextRunTime IS NOT NULL
            AND NextRunTime <= @Now";

        var jobs = await conn.QueryAsync<Job>(sql, new { Now = now });

        foreach (var job in jobs)
        {
            try
            {
                NormalizeJobDateTimes(job);

                // IMPORTANT: Update job state BEFORE calculating NextRunTime
                job.IsEnabled = true;
                job.ConsecutiveFailures = 0;
                job.Status = JobStatus.Idle;

                // Remove circuit-breaker tag properly
                if (!string.IsNullOrWhiteSpace(job.Tags))
                {
                    var tags = job.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Where(t => !string.Equals(t, "circuit-breaker", StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    job.Tags = string.Join(",", tags);
                }

                // Calculate NextRunTime with corrected state
                var nextRun = CalculateNextRunTime(job, now);

                var updateSql = @"
                    UPDATE Jobs
                    SET IsEnabled = 1,
                        ConsecutiveFailures = 0,
                        Status = @Idle,
                        NextRunTime = @NextRun,
                        Tags = @Tags,
                        ModifiedAt = @Now
                    WHERE Id = @JobId";

                await conn.ExecuteAsync(updateSql, new
                {
                    JobId = job.Id,
                    Idle = JobStatus.Idle,
                    NextRun = nextRun,
                    Tags = job.Tags,
                    Now = now
                });

                Log.Information("Circuit breaker reset: Re-enabled job {JobId} ({JobName}) after cooldown - NextRunTime: {NextRun}",
                    job.Id, job.Name, nextRun?.ToString("yyyy-MM-dd HH:mm:ss") ?? "null");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to re-enable circuit breaker job {JobId}", job.Id);
            }
        }
    }

    /// <summary>
    /// Resume a single auto-paused job (manual circuit breaker reset)
    /// </summary>
    public async Task ResumeCircuitBreakerJobAsync(int jobId)
    {
        using var conn = new SqliteConnection(_connectionString);

        var now = DateTime.UtcNow;

        var job = await conn.QuerySingleOrDefaultAsync<Job>(
            "SELECT * FROM Jobs WHERE Id = @Id", new { Id = jobId });

        if (job == null)
        {
            Log.Warning("Job {JobId} not found during ResumeCircuitBreakerJobAsync", jobId);
            return;
        }

        NormalizeJobDateTimes(job);

        // IMPORTANT: Update job state BEFORE calculating NextRunTime
        // Otherwise CalculateNextRunTime sees IsEnabled=false and returns NULL
        job.IsEnabled = true;
        job.ConsecutiveFailures = 0;
        job.Status = JobStatus.Idle;

        // Remove circuit-breaker tag properly (handle all cases)
        if (!string.IsNullOrWhiteSpace(job.Tags))
        {
            var tags = job.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(t => !string.Equals(t, "circuit-breaker", StringComparison.OrdinalIgnoreCase))
                .ToList();
            job.Tags = string.Join(",", tags);
        }

        // Now calculate NextRunTime with the corrected job state
        var nextRun = CalculateNextRunTime(job, now);

        if (!nextRun.HasValue && job.ScheduleType != ScheduleType.Manual)
        {
            Log.Error("Failed to calculate NextRunTime for job {JobId} after resume, forcing recalculation", jobId);
            nextRun = now.AddMinutes(5); // Safety fallback
        }

        var updateSql = @"
            UPDATE Jobs
            SET IsEnabled = 1,
                ConsecutiveFailures = 0,
                Status = @Idle,
                NextRunTime = @NextRun,
                Tags = @Tags,
                ModifiedAt = @Now
            WHERE Id = @JobId";

        await conn.ExecuteAsync(updateSql, new
        {
            JobId = jobId,
            Idle = JobStatus.Idle,
            NextRun = nextRun,
            Tags = job.Tags,
            Now = now
        });

        Log.Information("Job {JobId} ({JobName}) manually resumed - NextRunTime: {NextRun}, ConsecutiveFailures reset to 0",
            jobId, job.Name, nextRun?.ToString("yyyy-MM-dd HH:mm:ss") ?? "null");
    }

    /// <summary>
    /// Reset failure counter for a job (user acknowledgment)
    /// </summary>
    public async Task ResetFailureCounterAsync(int jobId)
    {
        using var conn = new SqliteConnection(_connectionString);

        var now = DateTime.UtcNow;

        await conn.ExecuteAsync(@"
            UPDATE Jobs
            SET ConsecutiveFailures = 0,
                ModifiedAt = @Now
            WHERE Id = @JobId",
            new { JobId = jobId, Now = now });

        Log.Information("Failure counter reset for job {JobId}", jobId);
    }

    /// <summary>
    ///  Recalculate and update NextRunTime for a job
    /// </summary>
    public async Task<IEnumerable<JobExecution>> GetExecutionHistoryAsync(int jobId, int limit = 50)
    {
        using var conn = new SqliteConnection(_connectionString);
        
        var sql = @"
            SELECT * FROM JobExecutions 
            WHERE JobId = @JobId 
            ORDER BY StartedAt DESC 
            LIMIT @Limit";
        
        return await conn.QueryAsync<JobExecution>(sql, new { JobId = jobId, Limit = limit });
    }

    public async Task<JobExecution?> GetLatestExecutionAsync(int jobId)
    {
        using var conn = new SqliteConnection(_connectionString);
        
        var sql = @"
            SELECT * FROM JobExecutions 
            WHERE JobId = @JobId 
            ORDER BY StartedAt DESC 
            LIMIT 1";
        
        return await conn.QuerySingleOrDefaultAsync<JobExecution>(sql, new { JobId = jobId });
    }

    public async Task<bool> CancelExecutionAsync(long executionId)
    {
        using var conn = new SqliteConnection(_connectionString);
        
        var sql = @"
            UPDATE JobExecutions 
            SET Status = @Cancelled, CompletedAt = @Now
            WHERE Id = @ExecutionId AND Status = @Running";
        
        var rows = await conn.ExecuteAsync(sql, new 
        { 
            ExecutionId = executionId, 
            Cancelled = JobStatus.Cancelled,
            Running = JobStatus.Running,
            Now = DateTime.UtcNow 
        });
        
        return rows > 0;
    }

    #endregion

    #region Schedule Calculation - CRITICAL FIXES HERE

    /// <summary>
    /// Calculate next run time for a job - convenience overload using current time
    /// </summary>
    public DateTime? CalculateNextRunTime(Job job)
    {
        return CalculateNextRunTime(job, DateTime.UtcNow);
    }

    /// <summary>
    /// Calculate next run time for a job - PRODUCTION-READY with all edge cases
    /// </summary>
    /// <param name="job">Job to calculate for</param>
    /// <param name="referenceTime">Reference time to calculate from (should be NOW, not LastRunTime)</param>
    /// <returns>Next run time in UTC, or null if job is disabled/manual</returns>
    public DateTime? CalculateNextRunTime(Job job, DateTime referenceTime)
    {
        // Edge case 1: Job is disabled or manual
        if (!job.IsEnabled || job.ScheduleType == ScheduleType.Manual)
        {
            return null;
        }
        
        // Ensure referenceTime is UTC
        if (referenceTime.Kind != DateTimeKind.Utc)
        {
            Log.Warning("Reference time for job {JobId} is not UTC, converting", job.Id);
            referenceTime = referenceTime.ToUniversalTime();
        }
        
        var now = DateTime.UtcNow;
        
        // Edge case 2: Job is outside its date range (ended)
        if (job.EndDate.HasValue && job.EndDate.Value < now)
        {
            Log.Debug("Job {JobId} has ended (EndDate: {EndDate})", job.Id, job.EndDate);
            return null;
        }
        
        // Use current time as base, NOT LastRunTime. This prevents the "next run in the past" bug.
        var baseTime = now;
        
        // Edge case 3: Job has a future start date
        if (job.StartDate.HasValue && baseTime < job.StartDate.Value)
        {
            baseTime = job.StartDate.Value;
            Log.Debug("Job {JobId} starts in the future at {StartDate}", job.Id, job.StartDate);
        }
        
        // Calculate based on schedule type
        DateTime? nextRun = job.ScheduleType switch
        {
            ScheduleType.Interval => CalculateIntervalNextRun(baseTime, job.IntervalMinutes ?? 60, job),
            ScheduleType.Cron => CalculateCronNextRun(baseTime, job.CronExpression!, job),
            ScheduleType.Daily => CalculateDailyNextRun(baseTime, job.StartTime, job),
            ScheduleType.Weekly => CalculateWeeklyNextRun(baseTime, job.StartTime, job),
            ScheduleType.Monthly => CalculateMonthlyNextRun(baseTime, job.StartTime, job),
            _ => null
        };
        
        // CRITICAL VALIDATION: Ensure nextRun is in the future
        if (nextRun.HasValue && nextRun.Value <= now)
        {
            Log.Warning("Calculated NextRun {NextRun} is not in future for job {JobId} ({JobName}), recalculating with buffer", 
                nextRun, job.Id, job.Name);
            
            // Force recalculation from current time + 1 minute
            var safeBaseTime = now.AddMinutes(1);
            nextRun = job.ScheduleType switch
            {
                ScheduleType.Interval => CalculateIntervalNextRun(safeBaseTime, job.IntervalMinutes ?? 60, job),
                ScheduleType.Cron => CalculateCronNextRun(safeBaseTime, job.CronExpression!, job),
                ScheduleType.Daily => CalculateDailyNextRun(safeBaseTime, job.StartTime, job),
                ScheduleType.Weekly => CalculateWeeklyNextRun(safeBaseTime, job.StartTime, job),
                ScheduleType.Monthly => CalculateMonthlyNextRun(safeBaseTime, job.StartTime, job),
                _ => null
            };
        }
        
        // Edge case 4: Respect job time window (StartTime/EndTime)
        if (nextRun.HasValue && job.StartTime.HasValue && job.EndTime.HasValue)
        {
            var nextRunTime = nextRun.Value.TimeOfDay;
            if (nextRunTime < job.StartTime.Value || nextRunTime > job.EndTime.Value)
            {
                // Move to next valid time window
                var nextValidTime = nextRun.Value.Date.Add(job.StartTime.Value);
                if (nextValidTime <= now)
                {
                    nextValidTime = nextValidTime.AddDays(1);
                }
                nextRun = nextValidTime;
                
                Log.Debug("Adjusted job {JobId} next run to respect time window: {NextRun}", 
                    job.Id, nextRun);
            }
        }
        
        // Final validation
        if (nextRun.HasValue && nextRun.Value <= now)
        {
            Log.Error("CRITICAL: NextRun {NextRun} is STILL in past for job {JobId} after all fixes! Forcing +1 hour", 
                nextRun, job.Id);
            nextRun = now.AddHours(1);
        }
        
        return nextRun;
    }

    private DateTime CalculateIntervalNextRun(DateTime baseTime, int intervalMinutes, Job job)
    {
        // Edge case: Zero or negative interval
        if (intervalMinutes <= 0)
        {
            Log.Warning("Job {JobId} has invalid interval {Interval}, defaulting to 60 minutes", 
                job.Id, intervalMinutes);
            intervalMinutes = 60;
        }
        
        // Always calculate from current time, not from last run
        // This prevents missed executions from piling up
        var now = DateTime.UtcNow;
        var nextRun = baseTime.AddMinutes(intervalMinutes);
        
        // If calculated time is in the past, keep adding intervals until it's in the future
        while (nextRun <= now)
        {
            nextRun = nextRun.AddMinutes(intervalMinutes);
        }
        
        return nextRun;
    }

    private DateTime? CalculateCronNextRun(DateTime baseTime, string cronExpression, Job job)
    {
        try
        {
            // Validate cron expression is not null or empty
            if (string.IsNullOrWhiteSpace(cronExpression))
            {
                Log.Error("Job {JobId} has empty cron expression", job.Id);
                return null;
            }
            
            // Parse with NCrontab (5-field cron: minute hour day month dayOfWeek)
            var schedule = CrontabSchedule.Parse(cronExpression);
            var nextOccurrence = schedule.GetNextOccurrence(baseTime);
            
            // Validate the next occurrence is in the future
            var now = DateTime.UtcNow;
            if (nextOccurrence <= now)
            {
                nextOccurrence = schedule.GetNextOccurrence(now);
            }
            
            return nextOccurrence;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Job {JobId} has invalid cron expression: {Cron}", job.Id, cronExpression);
            
            // Fallback: return 1 hour from now
            return baseTime.AddHours(1);
        }
    }

    private DateTime CalculateDailyNextRun(DateTime baseTime, TimeSpan? startTime, Job job)
    {
        var time = startTime ?? TimeSpan.Zero;
        var nextRun = baseTime.Date.Add(time);
        
        // If that time has passed today, move to tomorrow
        var now = DateTime.UtcNow;
        if (nextRun <= now)
        {
            nextRun = nextRun.AddDays(1);
        }
        
        return nextRun;
    }

    private DateTime CalculateWeeklyNextRun(DateTime baseTime, TimeSpan? startTime, Job job)
    {
        var time = startTime ?? TimeSpan.Zero;
        var now = DateTime.UtcNow;

        // Parse selected weekdays from job.WeekDays (comma-separated: 0=Monday, 6=Sunday)
        List<int> selectedDays = new();
        if (!string.IsNullOrEmpty(job.WeekDays))
        {
            selectedDays = job.WeekDays.Split(',')
                .Select(d => int.TryParse(d.Trim(), out var day) ? day : -1)
                .Where(d => d >= 0 && d <= 6)
                .OrderBy(d => d)
                .ToList();
        }

        // If no valid days specified, default to Monday
        if (selectedDays.Count == 0)
        {
            selectedDays = new List<int> { 0 };
        }

        // Find the next occurrence of one of the selected days
        var currentDay = (int)baseTime.DayOfWeek;

        // Adjust DayOfWeek to match our convention (0=Monday, 6=Sunday)
        // .NET uses 0=Sunday, 1=Monday, ..., 6=Saturday
        currentDay = currentDay == 0 ? 6 : currentDay - 1;

        // Find next matching day
        var daysAhead = 0;
        var found = false;

        for (var i = 0; i < 7; i++)
        {
            var checkDay = (currentDay + i) % 7;
            if (selectedDays.Contains(checkDay))
            {
                daysAhead = i;
                found = true;
                break;
            }
        }

        if (!found) daysAhead = 7;

        // If it's today, check if the time has already passed
        if (daysAhead == 0)
        {
            var todayAtTime = baseTime.Date.Add(time);
            if (todayAtTime <= now)
            {
                // Find next occurrence
                for (var i = 1; i <= 7; i++)
                {
                    var checkDay = (currentDay + i) % 7;
                    if (selectedDays.Contains(checkDay))
                    {
                        daysAhead = i;
                        break;
                    }
                }
            }
        }

        return baseTime.Date.AddDays(daysAhead).Add(time);
    }

    private DateTime CalculateMonthlyNextRun(DateTime baseTime, TimeSpan? startTime, Job job)
    {
        var time = startTime ?? TimeSpan.Zero;

        // Use job.MonthDay if specified, otherwise default to 1st
        var dayOfMonth = job.MonthDay ?? 1;

        // Clamp to valid day of month (1-31)
        dayOfMonth = Math.Max(1, Math.Min(31, dayOfMonth));

        var now = DateTime.UtcNow;

        // Try to create the date for this month
        var year = baseTime.Year;
        var month = baseTime.Month;

        // Handle case where day doesn't exist in this month (e.g., Feb 31)
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var actualDay = Math.Min(dayOfMonth, daysInMonth);

        var nextRun = new DateTime(year, month, actualDay, 0, 0, 0, DateTimeKind.Utc).Add(time);

        // If that time has passed this month, move to next month
        if (nextRun <= now)
        {
            // Try next month
            month++;
            if (month > 12)
            {
                month = 1;
                year++;
            }

            daysInMonth = DateTime.DaysInMonth(year, month);
            actualDay = Math.Min(dayOfMonth, daysInMonth);
            nextRun = new DateTime(year, month, actualDay, 0, 0, 0, DateTimeKind.Utc).Add(time);
        }

        return nextRun;
    }

    /// <summary>
    /// Utility method to recalculate and update NextRunTime for a job
    /// </summary>
    public async Task RecalculateAndUpdateNextRunTimeAsync(int jobId)
    {
        var job = await GetByIdAsync(jobId);
        if (job == null) return;
        
        var now = DateTime.UtcNow;
        var newNextRunTime = CalculateNextRunTime(job, now);
        
        await UpdateNextRunTimeAsync(jobId, newNextRunTime);
        
        Log.Information("Recalculated NextRunTime for job {JobId}: {NextRun}", 
            jobId, newNextRunTime?.ToString("yyyy-MM-dd HH:mm:ss") ?? "null");
    }

    #endregion

    #region Query Methods

    public async Task<IEnumerable<Job>> GetJobsByStatusAsync(JobStatus status)
    {
        using var conn = new SqliteConnection(_connectionString);
        var sql = "SELECT * FROM Jobs WHERE Status = @Status ORDER BY Priority DESC";
        var jobs = (await conn.QueryAsync<Job>(sql, new { Status = status })).ToList();
        
        foreach (var job in jobs)
        {
            NormalizeJobDateTimes(job);
        }
        
        return jobs;
    }

    public async Task<Dictionary<JobStatus, int>> GetJobStatusSummaryAsync()
    {
        using var conn = new SqliteConnection(_connectionString);
        var sql = "SELECT Status, COUNT(*) as Count FROM Jobs GROUP BY Status";
        var results = await conn.QueryAsync<dynamic>(sql);
        
        return results.ToDictionary(
            r => (JobStatus)r.Status, 
            r => (int)r.Count
        );
    }

    #endregion

    #region Cleanup

    /// <summary>
    /// Startup cleanup: Fix any jobs with NextRunTime in the past
    /// Call this on application startup
    /// </summary>
    public async Task FixCorruptedNextRunTimesAsync()
    {
        using var conn = new SqliteConnection(_connectionString);

        var now = DateTime.UtcNow;

        var sql = @"
            SELECT * FROM Jobs 
            WHERE IsEnabled = 1 
            AND ScheduleType != @ManualSchedule
            AND (NextRunTime IS NULL OR NextRunTime < @Now)";

        var corruptedJobs = (await conn.QueryAsync<Job>(sql, new { Now = now, ManualSchedule = ScheduleType.Manual })).ToList();

        foreach (var job in corruptedJobs)
        {
            NormalizeJobDateTimes(job);

            if (job.NextRunTime == null)
            {
                Log.Warning("Found job {JobId} ({JobName}) with NULL NextRunTime, fixing",
                    job.Id, job.Name);
            }
            else
            {
                Log.Warning("Found job {JobId} ({JobName}) with NextRunTime in past: {NextRun}, fixing",
                    job.Id, job.Name, job.NextRunTime);
            }

            await RecalculateAndUpdateNextRunTimeAsync(job.Id);

            if (job.Status == JobStatus.Failed)
            {
                await UpdateStatusAsync(job.Id, JobStatus.Idle);
                await conn.ExecuteAsync(
                    "UPDATE Jobs SET ConsecutiveFailures = 0 WHERE Id = @Id",
                    new { Id = job.Id });
            }
        }

        if (corruptedJobs.Any())
        {
            Log.Information("Fixed {Count} jobs with corrupted/null NextRunTime", corruptedJobs.Count());
        }
    }
    
    #endregion
}