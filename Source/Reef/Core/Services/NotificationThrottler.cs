// Source/Reef/Core/Services/NotificationThrottler.cs
// Service to prevent notification spam and mail server flooding
// Uses rate limiting and deduplication with configurable cooldown periods

using Serilog;
using System.Collections.Concurrent;

namespace Reef.Core.Services;

/// <summary>
/// Manages notification throttling and deduplication to prevent mail server flooding
/// Tracks the last time a notification was sent for each event type/key combination
/// </summary>
public class NotificationThrottler
{
    private static readonly Serilog.ILogger Log = Serilog.Log.ForContext<NotificationThrottler>();

    // Cooldown periods (in seconds) - adjust based on your needs
    public static class CooldownPeriods
    {
        // Profile execution failures: don't send more than once per 5 minutes per profile
        public const int ProfileFailure = 300;

        // Profile execution success: don't send more than once per 30 minutes per profile
        public const int ProfileSuccess = 1800;

        // Job failures: don't send more than once per 5 minutes per job
        public const int JobFailure = 300;

        // Job success: don't send more than once per 30 minutes per job
        public const int JobSuccess = 1800;

        // Database size threshold: only once per hour (expensive check)
        public const int DatabaseSizeThreshold = 3600;

        // User creation: send immediately (no throttling, expected to be rare)
        public const int UserCreation = 0;

        // API key creation: send immediately (no throttling, expected to be rare)
        public const int ApiKeyCreation = 0;

        // Webhook creation: send immediately (no throttling, expected to be rare)
        public const int WebhookCreation = 0;
    }

    // Thread-safe cache: "event_type:key" -> last sent timestamp
    private readonly ConcurrentDictionary<string, DateTime> _lastSentTime =
        new ConcurrentDictionary<string, DateTime>();

    // Cleanup task to remove expired entries every 10 minutes
    private CancellationTokenSource? _cleanupCts;

    public NotificationThrottler()
    {
        // Start background cleanup task
        StartCleanupTask();
    }

    /// <summary>
    /// Check if a notification should be sent based on rate limiting
    /// </summary>
    /// <param name="eventType">Type of notification (e.g., "ProfileFailure")</param>
    /// <param name="key">Unique key for this event (e.g., profile ID)</param>
    /// <param name="cooldownSeconds">Cooldown period in seconds (0 = no throttling)</param>
    /// <returns>True if notification should be sent, False if throttled</returns>
    public bool ShouldNotify(string eventType, string key, int cooldownSeconds)
    {
        // No throttling required
        if (cooldownSeconds <= 0)
        {
            Log.Debug("Notification not throttled: {EventType}:{Key}", eventType, key);
            return true;
        }

        var cacheKey = $"{eventType}:{key}";
        var now = DateTime.UtcNow;

        // Check if we have a recent notification for this event
        if (_lastSentTime.TryGetValue(cacheKey, out var lastSent))
        {
            var elapsedSeconds = (now - lastSent).TotalSeconds;

            if (elapsedSeconds < cooldownSeconds)
            {
                var remainingSeconds = cooldownSeconds - (int)elapsedSeconds;
                Log.Debug(
                    "Notification throttled (cooldown): {EventType}:{Key} - Wait {RemainingSeconds}s",
                    eventType, key, remainingSeconds);
                return false;
            }
        }

        // Allowed - update the timestamp
        _lastSentTime.AddOrUpdate(cacheKey, now, (_, __) => now);
        Log.Debug("Notification allowed: {EventType}:{Key}", eventType, key);
        return true;
    }

    /// <summary>
    /// Clear throttle history for a specific event (useful for testing or manual triggers)
    /// </summary>
    public void ClearThrottle(string eventType, string key)
    {
        var cacheKey = $"{eventType}:{key}";
        _lastSentTime.TryRemove(cacheKey, out _);
        Log.Information("Throttle cleared: {CacheKey}", cacheKey);
    }

    /// <summary>
    /// Clear all throttle history
    /// </summary>
    public void ClearAllThrottles()
    {
        _lastSentTime.Clear();
        Log.Information("All throttles cleared");
    }

    /// <summary>
    /// Get current throttle state for diagnostics
    /// </summary>
    public Dictionary<string, DateTime> GetThrottleState()
    {
        return new Dictionary<string, DateTime>(_lastSentTime);
    }

    /// <summary>
    /// Start background task to clean up old entries
    /// </summary>
    private void StartCleanupTask()
    {
        _cleanupCts = new CancellationTokenSource();

        _ = Task.Run(async () =>
        {
            try
            {
                while (!_cleanupCts.Token.IsCancellationRequested)
                {
                    // Cleanup every 10 minutes
                    await Task.Delay(TimeSpan.FromMinutes(10), _cleanupCts.Token);

                    var now = DateTime.UtcNow;
                    var entriesToRemove = _lastSentTime
                        .Where(kvp => (now - kvp.Value).TotalSeconds > 86400) // Remove entries older than 24 hours
                        .Select(kvp => kvp.Key)
                        .ToList();

                    foreach (var key in entriesToRemove)
                    {
                        _lastSentTime.TryRemove(key, out _);
                    }

                    if (entriesToRemove.Count > 0)
                    {
                        Log.Debug("Cleaned {Count} old throttle entries", entriesToRemove.Count);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Task was cancelled, this is expected
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in throttle cleanup task");
            }
        }, _cleanupCts.Token);
    }

    /// <summary>
    /// Dispose and stop cleanup task
    /// </summary>
    public void Dispose()
    {
        _cleanupCts?.Cancel();
        _cleanupCts?.Dispose();
        _lastSentTime.Clear();
    }
}

/// <summary>
/// Extension methods for NotificationThrottler to make usage cleaner
/// </summary>
public static class NotificationThrottlerExtensions
{
    /// <summary>
    /// Check if profile execution failure notification should be sent
    /// </summary>
    public static bool ShouldNotifyProfileFailure(this NotificationThrottler throttler, int profileId)
    {
        return throttler.ShouldNotify(
            "ProfileFailure",
            profileId.ToString(),
            NotificationThrottler.CooldownPeriods.ProfileFailure);
    }

    /// <summary>
    /// Check if profile execution success notification should be sent
    /// </summary>
    public static bool ShouldNotifyProfileSuccess(this NotificationThrottler throttler, int profileId)
    {
        return throttler.ShouldNotify(
            "ProfileSuccess",
            profileId.ToString(),
            NotificationThrottler.CooldownPeriods.ProfileSuccess);
    }

    /// <summary>
    /// Check if job failure notification should be sent
    /// </summary>
    public static bool ShouldNotifyJobFailure(this NotificationThrottler throttler, int jobId)
    {
        return throttler.ShouldNotify(
            "JobFailure",
            jobId.ToString(),
            NotificationThrottler.CooldownPeriods.JobFailure);
    }

    /// <summary>
    /// Check if job success notification should be sent
    /// </summary>
    public static bool ShouldNotifyJobSuccess(this NotificationThrottler throttler, int jobId)
    {
        return throttler.ShouldNotify(
            "JobSuccess",
            jobId.ToString(),
            NotificationThrottler.CooldownPeriods.JobSuccess);
    }

    /// <summary>
    /// Check if database size threshold notification should be sent
    /// </summary>
    public static bool ShouldNotifyDatabaseSize(this NotificationThrottler throttler)
    {
        return throttler.ShouldNotify(
            "DatabaseSize",
            "threshold",
            NotificationThrottler.CooldownPeriods.DatabaseSizeThreshold);
    }

    /// <summary>
    /// Check if import profile execution failure notification should be sent
    /// </summary>
    public static bool ShouldNotifyImportProfileFailure(this NotificationThrottler throttler, int profileId)
    {
        return throttler.ShouldNotify(
            "ImportProfileFailure",
            profileId.ToString(),
            NotificationThrottler.CooldownPeriods.ProfileFailure);
    }

    /// <summary>
    /// Check if import profile execution success notification should be sent
    /// </summary>
    public static bool ShouldNotifyImportProfileSuccess(this NotificationThrottler throttler, int profileId)
    {
        return throttler.ShouldNotify(
            "ImportProfileSuccess",
            profileId.ToString(),
            NotificationThrottler.CooldownPeriods.ProfileSuccess);
    }
}
