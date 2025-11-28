using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using NCrontab;
using Reef.Core.Models;
using Serilog;

namespace Reef.Core.Services;

/// <summary>
/// Background service for scheduled Profile execution
/// Checks for profiles that need to run based on their schedule configuration
/// </summary>
public class SchedulerService : BackgroundService
{
    private readonly string _connectionString;
    private readonly IServiceProvider _serviceProvider;
    private readonly int _checkIntervalSeconds;

    public SchedulerService(DatabaseConfig config, IServiceProvider serviceProvider, IConfiguration configuration)
    {
        _connectionString = config.ConnectionString;
        _serviceProvider = serviceProvider;
        
        // Read configuration value with sensible default
        _checkIntervalSeconds = configuration.GetValue<int>("Reef:Scheduler:CheckIntervalSeconds", 60);
        
        // Validate configuration
        if (_checkIntervalSeconds < 10 || _checkIntervalSeconds > 3600)
        {
            Log.Warning("Reef:Scheduler:CheckIntervalSeconds ({Value}) out of recommended range (10-3600 seconds), using default: 60", _checkIntervalSeconds);
            _checkIntervalSeconds = 60;
        }
        
        Log.Information("SchedulerService initialized with {Interval}s check interval", _checkIntervalSeconds);
    }

    /// <summary>
    /// Background execution loop
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Log.Information("Scheduler service starting");

        // Wait a few seconds for other services to initialize
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckAndExecuteScheduledProfilesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in scheduler service loop");
            }

            // Wait before next check
            await Task.Delay(TimeSpan.FromSeconds(_checkIntervalSeconds), stoppingToken);
        }

        Log.Information("Scheduler service stopping");
    }

    /// <summary>
    /// Check for scheduled profiles that need to run and execute them
    /// </summary>
    private async Task CheckAndExecuteScheduledProfilesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var now = DateTime.UtcNow;
            
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);

            // Find scheduled tasks that are due to run
            const string sql = @"
                SELECT st.*, p.Name as ProfileName, p.IsEnabled
                FROM ScheduledTasks st
                INNER JOIN Profiles p ON st.ProfileId = p.Id
                WHERE st.NextRunAt <= @Now
                  AND st.IsRunning = 0
                  AND p.IsEnabled = 1
                ORDER BY st.NextRunAt";

            var tasks = await connection.QueryAsync<ScheduledTaskWithProfile>(sql, new { Now = now });

            foreach (var task in tasks)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    Log.Information("Executing scheduled profile: {ProfileName} (ID: {ProfileId})", 
                        task.ProfileName, task.ProfileId);

                    // Mark as running
                    await MarkTaskAsRunningAsync(task.Id, true);

                    // Execute the profile
                    using var scope = _serviceProvider.CreateScope();
                    var executionService = scope.ServiceProvider.GetRequiredService<ExecutionService>();
                    var profileService = scope.ServiceProvider.GetRequiredService<ProfileService>();

                    var (executionId, success, outputPath, errorMessage) = await executionService.ExecuteProfileAsync(
                        task.ProfileId, 
                        parameters: null, 
                        triggeredBy: "Scheduler");

                    if (success)
                    {
                        Log.Information("Scheduled execution completed successfully. ExecutionId: {ExecutionId}", executionId);
                        
                        // Reset failure count on success
                        await UpdateScheduledTaskAsync(task.Id, task.ProfileId, 0, null);
                    }
                    else
                    {
                        Log.Warning("Scheduled execution failed: {ErrorMessage}", errorMessage);
                        
                        // Increment failure count
                        var failureCount = task.FailureCount + 1;
                        await UpdateScheduledTaskAsync(task.Id, task.ProfileId, failureCount, errorMessage);

                        // If too many failures, disable the profile or alert
                        if (failureCount >= 5)
                        {
                            Log.Error("Profile {ProfileId} has failed {FailureCount} times. Consider investigation.", 
                                task.ProfileId, failureCount);
                        }
                    }

                    // Mark as not running
                    await MarkTaskAsRunningAsync(task.Id, false);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error executing scheduled task for profile {ProfileId}", task.ProfileId);
                    
                    // Mark as not running and update failure count
                    await MarkTaskAsRunningAsync(task.Id, false);
                    var failureCount = task.FailureCount + 1;
                    await UpdateScheduledTaskAsync(task.Id, task.ProfileId, failureCount, ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error checking scheduled profiles");
        }
    }

    /// <summary>
    /// Mark task as running or not running
    /// </summary>
    private async Task MarkTaskAsRunningAsync(int taskId, bool isRunning)
    {
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            const string sql = @"
                UPDATE ScheduledTasks 
                SET IsRunning = @IsRunning,
                    LastRunAt = @LastRunAt,
                    UpdatedAt = @UpdatedAt
                WHERE Id = @Id";

            await connection.ExecuteAsync(sql, new
            {
                Id = taskId,
                IsRunning = isRunning ? 1 : 0,
                LastRunAt = isRunning ? (DateTime?)DateTime.UtcNow : null,
                UpdatedAt = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error marking task {TaskId} as {Status}", taskId, isRunning ? "running" : "not running");
        }
    }

    /// <summary>
    /// Update scheduled task with next run time and failure tracking
    /// </summary>
    private async Task UpdateScheduledTaskAsync(int taskId, int profileId, int failureCount, string? lastError)
    {
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            // Get profile to calculate next run time
            var profile = await connection.QueryFirstOrDefaultAsync<Profile>(
                "SELECT * FROM Profiles WHERE Id = @Id", new { Id = profileId });

            if (profile == null)
            {
                Log.Warning("Profile {ProfileId} not found when updating scheduled task", profileId);
                return;
            }

            var nextRunAt = CalculateNextRunTime(profile);

            const string sql = @"
                UPDATE ScheduledTasks 
                SET NextRunAt = @NextRunAt,
                    IsRunning = 0,
                    FailureCount = @FailureCount,
                    LastError = @LastError,
                    LastRunAt = @LastRunAt,
                    UpdatedAt = @UpdatedAt
                WHERE Id = @Id";

            await connection.ExecuteAsync(sql, new
            {
                Id = taskId,
                NextRunAt = nextRunAt,
                FailureCount = failureCount,
                LastError = lastError,
                LastRunAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });

            Log.Debug("Scheduled task {TaskId} updated. Next run: {NextRunAt}", taskId, nextRunAt);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error updating scheduled task {TaskId}", taskId);
        }
    }

    /// <summary>
    /// Calculate next run time based on profile schedule configuration
    /// </summary>
    private DateTime CalculateNextRunTime(Profile profile)
    {
        var now = DateTime.UtcNow;

        try
        {
            if (profile.ScheduleType == "Cron" && !string.IsNullOrEmpty(profile.ScheduleCron))
            {
                // Parse cron expression using NCrontab
                var schedule = CrontabSchedule.Parse(profile.ScheduleCron);
                var nextOccurrence = schedule.GetNextOccurrence(now);
                
                Log.Debug("Next cron occurrence for profile {ProfileId}: {NextOccurrence}", 
                    profile.Id, nextOccurrence);
                
                return nextOccurrence;
            }
            else if (profile.ScheduleType == "Interval" && profile.ScheduleIntervalMinutes.HasValue)
            {
                var nextRun = now.AddMinutes(profile.ScheduleIntervalMinutes.Value);
                
                Log.Debug("Next interval run for profile {ProfileId}: {NextRun} ({Interval} minutes)", 
                    profile.Id, nextRun, profile.ScheduleIntervalMinutes.Value);
                
                return nextRun;
            }
            else
            {
                // Default: 1 hour from now
                Log.Warning("Profile {ProfileId} has invalid schedule configuration. Defaulting to 1 hour.", profile.Id);
                return now.AddHours(1);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error calculating next run time for profile {ProfileId}. Defaulting to 1 hour.", profile.Id);
            return now.AddHours(1);
        }
    }
}

/// <summary>
/// Extended scheduled task model with profile name
/// </summary>
public class ScheduledTaskWithProfile : ScheduledTask
{
    public string? ProfileName { get; set; }
    public bool IsEnabled { get; set; }
}   