using Dapper;
using Microsoft.Data.Sqlite;
using Serilog;
using Reef.Core.Abstractions;
using Reef.Core.Models;
using ILogger = Serilog.ILogger;

namespace Reef.Core.Services.Import;

/// <summary>
/// Service for managing import jobs (scheduling and execution)
/// Integrates with JobScheduler for automated import execution
/// </summary>
public class ImportJobService
{
    private readonly string _connectionString;
    private readonly IImportProfileService _profileService;
    private readonly ImportExecutionService _executionService;
    private static readonly ILogger Log = Serilog.Log.ForContext(typeof(ImportJobService));

    public ImportJobService(
        DatabaseConfig config,
        IImportProfileService profileService,
        ImportExecutionService executionService)
    {
        _connectionString = config.ConnectionString;
        _profileService = profileService;
        _executionService = executionService;
    }

    /// <summary>
    /// Get all import profiles that have scheduled execution enabled
    /// </summary>
    public async Task<List<ImportProfile>> GetScheduledProfilesAsync(CancellationToken cancellationToken = default)
    {
        using var db = new SqliteConnection(_connectionString);
        await db.OpenAsync(cancellationToken);

        var profiles = await db.QueryAsync<ImportProfile>(
            @"SELECT * FROM ImportProfiles
              WHERE IsEnabled = 1
                AND ScheduleType IS NOT NULL
                AND ScheduleType != ''
              ORDER BY LastExecutedAt ASC");

        return profiles.ToList();
    }

    /// <summary>
    /// Check if an import profile is due for execution based on its schedule
    /// </summary>
    public bool IsProfileDueForExecution(ImportProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.ScheduleType))
            return false;

        if (!profile.IsEnabled)
            return false;

        // Calculate next run time based on schedule type
        var nextRunTime = CalculateNextRunTime(profile);
        return nextRunTime <= DateTime.UtcNow;
    }

    /// <summary>
    /// Execute an import profile
    /// </summary>
    public async Task<ImportExecutionResult> ExecuteImportProfileAsync(
        int profileId,
        string triggeredBy = "Scheduled",
        CancellationToken cancellationToken = default)
    {
        try
        {
            Log.Information("Executing import profile {ProfileId} triggered by {TriggeredBy}", profileId, triggeredBy);

            var result = await _executionService.ExecuteAsync(profileId, triggeredBy, cancellationToken);

            // Update last executed time
            await UpdateLastExecutedTimeAsync(profileId, result.Status == "Success", cancellationToken);

            Log.Information("Import profile {ProfileId} execution completed: {Status}", profileId, result.Status);

            return result;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to execute import profile {ProfileId}", profileId);
            throw;
        }
    }

    /// <summary>
    /// Update the last executed time for a profile
    /// </summary>
    public async Task UpdateLastExecutedTimeAsync(
        int profileId,
        bool success,
        CancellationToken cancellationToken = default)
    {
        using var db = new SqliteConnection(_connectionString);
        await db.OpenAsync(cancellationToken);

        await db.ExecuteAsync(
            @"UPDATE ImportProfiles
              SET LastExecutedAt = @Now,
                  UpdatedAt = @Now
              WHERE Id = @ProfileId",
            new { ProfileId = profileId, Now = DateTime.UtcNow });

        Log.Debug("Updated last executed time for profile {ProfileId}", profileId);
    }

    /// <summary>
    /// Calculate the next run time for a profile based on its schedule
    /// </summary>
    private DateTime CalculateNextRunTime(ImportProfile profile)
    {
        var lastRun = profile.LastExecutedAt ?? DateTime.UtcNow.AddHours(-1);

        return profile.ScheduleType switch
        {
            "Cron" => CalculateCronNextRunTime(profile.ScheduleCron, lastRun),
            "Interval" => lastRun.AddMinutes(profile.ScheduleIntervalMinutes ?? 60),
            "Daily" => lastRun.Date.AddDays(1),
            "Weekly" => lastRun.AddDays(7),
            "Monthly" => lastRun.AddMonths(1),
            _ => DateTime.MaxValue
        };
    }

    /// <summary>
    /// Simple cron expression evaluation (supports basic patterns)
    /// Format: minute hour day month dayOfWeek
    /// Example: "0 12 * * *" = daily at noon
    /// </summary>
    private DateTime CalculateCronNextRunTime(string? cronExpression, DateTime lastRun)
    {
        if (string.IsNullOrWhiteSpace(cronExpression))
            return DateTime.MaxValue;

        try
        {
            // For now, simple fallback to interval-based
            // Full cron parsing would require a library like CronExpressionDescriptor
            Log.Debug("Cron expression parsing not fully implemented, using interval fallback for: {Cron}", cronExpression);

            // Default to every hour for unsupported cron expressions
            return lastRun.AddHours(1);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to parse cron expression: {Cron}", cronExpression);
            return DateTime.MaxValue;
        }
    }

    /// <summary>
    /// Pause an import profile (temporarily disable scheduling)
    /// </summary>
    public async Task PauseProfileAsync(int profileId, CancellationToken cancellationToken = default)
    {
        using var db = new SqliteConnection(_connectionString);
        await db.OpenAsync(cancellationToken);

        await db.ExecuteAsync(
            @"UPDATE ImportProfiles
              SET IsEnabled = 0,
                  UpdatedAt = @Now
              WHERE Id = @ProfileId",
            new { ProfileId = profileId, Now = DateTime.UtcNow });

        Log.Information("Import profile {ProfileId} paused", profileId);
    }

    /// <summary>
    /// Resume an import profile (enable scheduling)
    /// </summary>
    public async Task ResumeProfileAsync(int profileId, CancellationToken cancellationToken = default)
    {
        using var db = new SqliteConnection(_connectionString);
        await db.OpenAsync(cancellationToken);

        await db.ExecuteAsync(
            @"UPDATE ImportProfiles
              SET IsEnabled = 1,
                  UpdatedAt = @Now
              WHERE Id = @ProfileId",
            new { ProfileId = profileId, Now = DateTime.UtcNow });

        Log.Information("Import profile {ProfileId} resumed", profileId);
    }
}
