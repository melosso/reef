using Reef.Core.Models;
using Reef.Core.Services;
using Serilog;
using System.Text.Json;
using Microsoft.Extensions.Configuration;

namespace Reef.Api;

/// <summary>
/// Service for executing jobs and managing job execution lifecycle
/// </summary>
public class JobExecutorService
{
    private readonly JobService _jobService;
    private readonly ExecutionService _executionService;
    private readonly ProfileService _profileService;
    private readonly NotificationService _notificationService;
    private readonly IConfiguration _configuration;

    public JobExecutorService(
        JobService jobService,
        ExecutionService executionService,
        ProfileService profileService,
        NotificationService notificationService,
        IConfiguration configuration)
    {
        _jobService = jobService;
        _executionService = executionService;
        _profileService = profileService;
        _notificationService = notificationService;
        _configuration = configuration;
    }

    /// <summary>
    /// Trigger a job execution
    /// </summary>
    /// <param name="job">Job to execute</param>
    /// <param name="ignoreDependencies">Whether to ignore job dependencies</param>
    /// <param name="parameters">Optional parameters for job execution</param>
    /// <returns>Execution ID</returns>
    public async Task<long> TriggerJobAsync(
        Job job, 
        bool ignoreDependencies, 
        Dictionary<string, object>? parameters)
    {
        Log.Debug("Triggering job {JobId} ({JobName}) - Type: {JobType}", 
            job.Id, job.Name, job.Type);

        // Check if job is enabled
        if (!job.IsEnabled)
        {
            throw new InvalidOperationException($"Job {job.Id} is disabled and cannot be executed");
        }

        // Check concurrent execution policy
        if (!job.AllowConcurrent)
        {
            var latestExecution = await _jobService.GetLatestExecutionAsync(job.Id);
            if (latestExecution != null && latestExecution.Status == JobStatus.Running)
            {
                throw new InvalidOperationException(
                    $"Job {job.Id} is already running and does not allow concurrent execution");
            }
        }

        // Check dependencies if not ignored
        if (!ignoreDependencies && !string.IsNullOrEmpty(job.DependsOnJobIds))
        {
            await ValidateDependenciesAsync(job.DependsOnJobIds);
        }

        // Set job status to Running immediately
        await _jobService.UpdateStatusAsync(job.Id, JobStatus.Running);

        // Create execution record
        var execution = new JobExecution
        {
            JobId = job.Id,
            StartedAt = DateTime.UtcNow,
            Status = JobStatus.Running,
            AttemptNumber = job.ConsecutiveFailures + 1,
            TriggeredBy = "API",
            ServerNode = Environment.MachineName,
            ExecutionContext = parameters != null 
                ? JsonSerializer.Serialize(parameters) 
                : null
        };

        var executionId = await _jobService.CreateExecutionAsync(execution);
        execution.Id = executionId;

        // Execute the job asynchronously (fire and forget)
        _ = Task.Run(async () => await ExecuteJobInternalAsync(job, execution, parameters));

        return executionId;
    }

    /// <summary>
    /// Internal method to execute the job with retry logic
    /// MaxRetries applies per-cycle: retries happen within one scheduled execution
    /// Only after ALL retries fail does it count as 1 failure for the circuit breaker
    /// </summary>
    private async Task ExecuteJobInternalAsync(
        Job job,
        JobExecution execution,
        Dictionary<string, object>? parameters)
    {
        bool success = false;
        string? outputData = null;
        string? errorMessage = null;
        int? rowsProcessed = null;
        int attemptNumber = 0;
        var maxAttempts = job.MaxRetries + 1; // MaxRetries=3 means 4 total attempts (original + 3 retries)

        try
        {
            Log.Information("Starting execution {ExecutionId} for job {JobId} (max {MaxAttempts} attempts)",
                execution.Id, job.Id, maxAttempts);

            // Retry loop: attempt execution up to MaxRetries + 1 times
            for (attemptNumber = 1; attemptNumber <= maxAttempts; attemptNumber++)
            {
                try
                {
                    if (attemptNumber > 1)
                    {
                        // Exponential backoff between retries: 2^(attempt-1) seconds
                        var delaySeconds = Math.Pow(2, attemptNumber - 2); // 1s, 2s, 4s, 8s...
                        Log.Information("Job {JobId} execution {ExecutionId} retry attempt {Attempt}/{Max} after {Delay}s delay",
                            job.Id, execution.Id, attemptNumber, maxAttempts, delaySeconds);
                        await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
                    }

                    // Execute based on job type
                    (success, outputData, errorMessage, rowsProcessed) = await ExecuteJobByTypeAsync(
                        job, parameters);

                    if (success)
                    {
                        Log.Information("Job execution {ExecutionId} succeeded on attempt {Attempt}/{Max} with {Rows} rows",
                            execution.Id, attemptNumber, maxAttempts, rowsProcessed ?? 0);
                        break; // Success - exit retry loop
                    }
                    else
                    {
                        Log.Warning("Job execution {ExecutionId} attempt {Attempt}/{Max} failed: {Error}",
                            execution.Id, attemptNumber, maxAttempts, errorMessage);

                        // If this was the last attempt, it's a final failure
                        if (attemptNumber == maxAttempts)
                        {
                            Log.Error("Job execution {ExecutionId} failed after {Attempts} attempts: {Error}",
                                execution.Id, maxAttempts, errorMessage);
                        }
                    }
                }
                catch (Exception ex)
                {
                    errorMessage = ex.Message;
                    Log.Error(ex, "Job execution {ExecutionId} attempt {Attempt}/{Max} threw exception",
                        execution.Id, attemptNumber, maxAttempts);

                    if (attemptNumber == maxAttempts)
                    {
                        throw; // Re-throw on final attempt to be caught by outer catch
                    }
                }
            }

            // Update execution record with final result
            execution.Status = success ? JobStatus.Completed : JobStatus.Failed;
            execution.OutputData = outputData;
            execution.ErrorMessage = errorMessage;
            execution.RowsProcessed = rowsProcessed;
            execution.AttemptNumber = attemptNumber;
            execution.CompletedAt = DateTime.UtcNow;

            await _jobService.UpdateExecutionAsync(execution);

            // Send job notification (fire and forget)
            _ = Task.Run(async () =>
            {
                try
                {
                    if (success)
                    {
                        await _notificationService.SendJobSuccessAsync(
                            job.Id,
                            job.Name,
                            new Dictionary<string, object>
                            {
                                { "RowsProcessed", rowsProcessed ?? 0 },
                                { "Duration", execution.CompletedAt.HasValue ? (execution.CompletedAt.Value - execution.StartedAt).TotalSeconds : 0 }
                            });
                    }
                    else
                    {
                        await _notificationService.SendJobFailureAsync(job.Id, job.Name, errorMessage ?? "Unknown error");
                    }
                }
                catch (Exception notifEx) { Log.Error(notifEx, "Failed to send job notification"); }
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Unexpected error executing job {JobId} after {Attempts} attempts",
                job.Id, attemptNumber);

            execution.Status = JobStatus.Failed;
            execution.ErrorMessage = ex.Message;
            execution.StackTrace = ex.StackTrace;
            execution.AttemptNumber = attemptNumber;
            execution.CompletedAt = DateTime.UtcNow;

            await _jobService.UpdateExecutionAsync(execution);

            // Send job failure notification (fire and forget)
            _ = Task.Run(async () =>
            {
                try
                {
                    await _notificationService.SendJobFailureAsync(job.Id, job.Name, ex.Message);
                }
                catch (Exception notifEx) { Log.Error(notifEx, "Failed to send job failure notification"); }
            });
        }
    }

    /// <summary>
    /// Execute job based on its type
    /// </summary>
    private async Task<(bool Success, string? OutputData, string? ErrorMessage, int? RowsProcessed)> 
        ExecuteJobByTypeAsync(Job job, Dictionary<string, object>? parameters)
    {
        try
        {
            switch (job.Type)
            {
                case JobType.ProfileExecution:
                    return await ExecuteProfileJobAsync(job, parameters);

                case JobType.HealthCheck:
                    return await ExecuteHealthCheckJobAsync();

                case JobType.BackupDatabase:
                    return await ExecuteBackupDatabaseJobAsync();

                case JobType.Cleanup:
                    return await ExecuteCleanupJobAsync();

                default:
                    return (false, null, $"Job type {job.Type} is not yet implemented", null);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error executing job type {JobType}", job.Type);
            return (false, null, ex.Message, null);
        }
    }

    /// <summary>
    /// Execute a profile-based job
    /// </summary>
    private async Task<(bool Success, string? OutputData, string? ErrorMessage, int? RowsProcessed)>
        ExecuteProfileJobAsync(Job job, Dictionary<string, object>? parameters)
    {
        if (!job.ProfileId.HasValue)
        {
            return (false, null, "ProfileId is required for ProfileExecution jobs", null);
        }

        // Convert parameters to string dictionary for ExecutionService
        Dictionary<string, string>? stringParams = null;
        if (parameters != null)
        {
            stringParams = parameters.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value?.ToString() ?? string.Empty
            );
        }

        var (executionId, success, outputPath, errorMessage) = 
            await _executionService.ExecuteProfileAsync(
                job.ProfileId.Value, 
                stringParams, 
                $"Job-{job.Id}",
                job.Id,              // Pass job ID for tracking
                job.DestinationId);  // Pass the job's destination override

        var outputData = success && outputPath != null
            ? JsonSerializer.Serialize(new { executionId, outputPath })
            : null;

        // Try to get row count from profile execution if available
        int? rowsProcessed = null;
        if (success && executionId > 0)
        {
            // The ExecutionService doesn't return row count, but we can try to query it
            // For now, we'll leave it null
        }

        return (success, outputData, errorMessage, rowsProcessed);
    }

    /// <summary>
    /// Execute health check job
    /// </summary>
    private Task<(bool Success, string? OutputData, string? ErrorMessage, int? RowsProcessed)>
        ExecuteHealthCheckJobAsync()
    {
        try
        {
            // Perform basic health checks
            var healthData = new
            {
                timestamp = DateTime.UtcNow,
                server = Environment.MachineName,
                uptime = Environment.TickCount64 / 1000, // seconds
                memoryUsage = GC.GetTotalMemory(false),
                status = "healthy"
            };

            var outputData = JsonSerializer.Serialize(healthData);
            Log.Information("Health check completed: {Status}", healthData.status);

            return Task.FromResult<(bool, string?, string?, int?)>((true, outputData, null, null));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Health check failed");
            return Task.FromResult<(bool, string?, string?, int?)>((false, null, ex.Message, null));
        }
    }

    /// <summary>
    /// Execute database backup job
    /// </summary>
    private async Task<(bool Success, string? OutputData, string? ErrorMessage, int? RowsProcessed)>
        ExecuteBackupDatabaseJobAsync()
    {
        try
        {
            // Get database path from configuration
            var configuredPath = _configuration["Reef:DatabasePath"] ?? "Reef.db";
            var connectionString = _configuration.GetConnectionString("Reef") ?? $"Data Source={configuredPath}";

            // Extract database path from connection string
            var dbPath = configuredPath;
            if (connectionString.Contains("Data Source=", StringComparison.OrdinalIgnoreCase))
            {
                var dataSourceStart = connectionString.IndexOf("Data Source=", StringComparison.OrdinalIgnoreCase) + "Data Source=".Length;
                var semicolonIndex = connectionString.IndexOf(';', dataSourceStart);
                dbPath = semicolonIndex > 0
                    ? connectionString.Substring(dataSourceStart, semicolonIndex - dataSourceStart).Trim()
                    : connectionString.Substring(dataSourceStart).Trim();
            }

            var backupDir = "backups";
            
            if (!Directory.Exists(backupDir))
            {
                Directory.CreateDirectory(backupDir);
            }

            var backupFileName = $"reef_backup_{DateTime.UtcNow:yyyyMMdd_HHmmss}.db";
            var backupPath = Path.Combine(backupDir, backupFileName);

            // Perform the backup on a background thread since File.Copy is synchronous
            await Task.Run(() => File.Copy(dbPath, backupPath, overwrite: false));

            var fileInfo = new FileInfo(backupPath);
            var outputData = JsonSerializer.Serialize(new
            {
                backupPath,
                fileSizeBytes = fileInfo.Length,
                timestamp = DateTime.UtcNow
            });

            Log.Information("Database backup created: {BackupPath}", backupPath);
            return (true, outputData, null, null);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Database backup failed");
            return (false, null, ex.Message, null);
        }
    }

    /// <summary>
    /// Execute cleanup job
    /// </summary>
    private Task<(bool Success, string? OutputData, string? ErrorMessage, int? RowsProcessed)>
        ExecuteCleanupJobAsync()
    {
        try
        {
            // Clean up old execution records (older than 90 days)
            var cutoffDate = DateTime.UtcNow.AddDays(-90);
            var deletedCount = 0;

            // This would require a method in JobService to delete old executions
            // For now, we'll just log the intent
            Log.Information("Cleanup job would delete executions older than {CutoffDate}", cutoffDate);

            var outputData = JsonSerializer.Serialize(new
            {
                deletedRecords = deletedCount,
                cutoffDate,
                timestamp = DateTime.UtcNow
            });

            return Task.FromResult<(bool, string?, string?, int?)>((true, outputData, null, deletedCount));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Cleanup job failed");
            return Task.FromResult<(bool, string?, string?, int?)>((false, null, ex.Message, null));
        }
    }

    /// <summary>
    /// Validate job dependencies
    /// </summary>
    private async Task ValidateDependenciesAsync(string dependsOnJobIds)
    {
        var jobIds = dependsOnJobIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(id => int.Parse(id.Trim()))
            .ToList();

        foreach (var jobId in jobIds)
        {
            var job = await _jobService.GetByIdAsync(jobId);
            if (job == null)
            {
                throw new InvalidOperationException($"Dependency job {jobId} not found");
            }

            var latestExecution = await _jobService.GetLatestExecutionAsync(jobId);
            if (latestExecution == null || latestExecution.Status != JobStatus.Completed)
            {
                throw new InvalidOperationException(
                    $"Dependency job {jobId} has not completed successfully");
            }
        }

        Log.Information("All job dependencies validated successfully");
    }
}