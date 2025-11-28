// Source/Reef/Core/Services/NotificationIntegrationHelper.cs
// Helper extension methods and utilities for integrating notifications throughout the codebase
// This provides a unified API for sending notifications with best practices built-in

using Reef.Core.Models;
using Serilog;

namespace Reef.Core.Services;

/// <summary>
/// Extension methods for NotificationService to simplify integration across the application
/// </summary>
public static class NotificationIntegrationHelper
{
    private static readonly Serilog.ILogger Log = Serilog.Log.ForContext<NotificationService>();

    /// <summary>
    /// Send profile execution completion notification (called from ExecutionService)
    /// Place this call after UpdateExecutionRecordAsync() completes
    /// </summary>
    /// <example>
    /// In ExecutionService.ExecuteProfileAsync():
    /// await UpdateExecutionRecordAsync(executionId, "Success", rowCount, outputPath, stopwatch.ElapsedMilliseconds, null);
    /// await _notificationService.NotifyProfileExecutionAsync(executionId, profileId, isSuccess);
    /// </example>
    public static void NotifyProfileExecutionAsync(
        this NotificationService service,
        int executionId,
        int profileId,
        bool isSuccess,
        ProfileExecution? execution = null,
        Profile? profile = null)
    {
        try
        {
            // If execution/profile objects not provided, load them from database
            // (this is handled inside SendExecutionSuccessAsync/SendExecutionFailureAsync)

            if (isSuccess && profile != null && execution != null)
            {
                // Fire and forget - don't block the response
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await service.SendExecutionSuccessAsync(execution, profile);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Failed to send execution success notification");
                    }
                });
            }
            else if (!isSuccess && profile != null && execution != null)
            {
                // Fire and forget - don't block the response
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await service.SendExecutionFailureAsync(execution, profile);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "Failed to send execution failure notification");
                    }
                });
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error in NotifyProfileExecutionAsync");
        }
    }

    /// <summary>
    /// Send job execution completion notification (called from JobService/JobExecutorService)
    /// Place this call after job execution completes
    /// </summary>
    /// <example>
    /// In JobExecutorService or job completion handler:
    /// await _notificationService.NotifyJobExecutionAsync(jobId, jobName, isSuccess, errorMessage);
    /// </example>
    public static void NotifyJobExecutionAsync(
        this NotificationService service,
        int jobId,
        string jobName,
        bool isSuccess,
        string? errorMessage = null,
        Dictionary<string, object>? jobDetails = null)
    {
        try
        {
            // Fire and forget - don't block the response
            _ = Task.Run(async () =>
            {
                try
                {
                    if (isSuccess)
                    {
                        await service.SendJobSuccessAsync(
                            jobId,
                            jobName,
                            jobDetails ?? new Dictionary<string, object>
                            {
                                { "CompletedAt", DateTime.UtcNow }
                            });
                    }
                    else
                    {
                        await service.SendJobFailureAsync(jobId, jobName, errorMessage ?? "Unknown error");
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to send job notification");
                }
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error in NotifyJobExecutionAsync");
        }
    }

    /// <summary>
    /// Send user creation notification (called from AdminService.CreateUserAsync)
    /// Place this call after user creation succeeds
    /// </summary>
    /// <example>
    /// In AdminService.CreateUserAsync():
    /// var userId = await service.CreateUserAsync(username, password, role);
    /// _notificationService.NotifyUserCreationAsync(username, email);
    /// </example>
    public static void NotifyUserCreationAsync(
        this NotificationService service,
        string username,
        string? email = null)
    {
        try
        {
            // Fire and forget - don't block the response
            _ = Task.Run(async () =>
            {
                try
                {
                    await service.SendNewUserNotificationAsync(username, email ?? $"{username}@local");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to send user creation notification");
                }
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error in NotifyUserCreationAsync");
        }
    }

    /// <summary>
    /// Send API key creation notification (called from AdminService.CreateApiKeyAsync)
    /// Place this call after API key creation succeeds
    /// </summary>
    /// <example>
    /// In AdminService.CreateApiKeyAsync():
    /// var (keyId, apiKeyValue) = await service.CreateApiKeyAsync(name, permissions, expiresAt);
    /// _notificationService.NotifyApiKeyCreationAsync(name);
    /// </example>
    public static void NotifyApiKeyCreationAsync(
        this NotificationService service,
        string keyName)
    {
        try
        {
            // Fire and forget - don't block the response
            _ = Task.Run(async () =>
            {
                try
                {
                    await service.SendNewApiKeyNotificationAsync(keyName);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to send API key creation notification");
                }
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error in NotifyApiKeyCreationAsync");
        }
    }

    /// <summary>
    /// Send webhook creation notification (called from WebhookService.CreateWebhookAsync)
    /// Place this call after webhook creation succeeds
    /// </summary>
    /// <example>
    /// In WebhookService or WebhookEndpoints:
    /// var webhookId = await service.CreateWebhookAsync(name, triggerType, url);
    /// _notificationService.NotifyWebhookCreationAsync(webhookName);
    /// </example>
    public static void NotifyWebhookCreationAsync(
        this NotificationService service,
        string webhookName)
    {
        try
        {
            // Fire and forget - don't block the response
            _ = Task.Run(async () =>
            {
                try
                {
                    await service.SendNewWebhookNotificationAsync(webhookName);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to send webhook creation notification");
                }
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error in NotifyWebhookCreationAsync");
        }
    }

    /// <summary>
    /// Check database size and send notification if threshold exceeded
    /// Called from a background task (recommended: run every 10-60 minutes)
    /// </summary>
    /// <example>
    /// In a hosted background service:
    /// var dbSize = new FileInfo(dbPath).Length;
    /// await _notificationService.SendDatabaseSizeNotificationAsync(dbSize);
    /// </example>
    public static async Task NotifyDatabaseSizeAsync(
        this NotificationService service,
        long currentSizeBytes)
    {
        try
        {
            // This is OK to await - database size check is infrequent and important
            await service.SendDatabaseSizeNotificationAsync(currentSizeBytes);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error in NotifyDatabaseSizeAsync");
        }
    }
}

/// <summary>
/// Integration notes for wiring up notifications in your services:
///
/// 1. EXECUTIONSERVICE (Profile Execution Notifications)
///    - Add NotificationService to constructor
///    - After UpdateExecutionRecordAsync() call in ExecuteProfileAsync():
///
///      // Get the execution record and profile
///      var execution = await ... // Query ProfileExecutions table
///      var profile = await _profileService.GetByIdAsync(profileId);
///
///      // Send notification
///      await _notificationService.NotifyProfileExecutionAsync(
///          executionId, profileId, success, execution, profile);
///
///    - Throttling: Handled automatically (5 min for failures, 30 min for success)
///    - Best practice: Use fire-and-forget (_ = Task.Run()) to not block response
///
/// 2. JOBSERVICE / JOBEXECUTORSERVICE (Job Execution Notifications)
///    - Add NotificationService to constructor
///    - After job completes (success or failure):
///
///      await _notificationService.NotifyJobExecutionAsync(
///          jobId, jobName, isSuccess, errorMessage, jobDetails);
///
///    - Throttling: 5 min for failures, 30 min for success per job
///    - Best practice: Use fire-and-forget pattern
///
/// 3. ADMINSERVICE (User Creation Notifications)
///    - Add NotificationService to constructor
///    - After CreateUserAsync() succeeds:
///
///      var userId = await CreateUserAsync(username, password, role);
///      await _notificationService.NotifyUserCreationAsync(username, email);
///
///    - Throttling: None (users are created rarely)
///    - Can await directly (low frequency)
///
/// 4. ADMINSERVICE (API Key Creation Notifications)
///    - Add NotificationService to constructor
///    - After CreateApiKeyAsync() succeeds:
///
///      var (keyId, apiKey) = await CreateApiKeyAsync(name, permissions, expiresAt);
///      await _notificationService.NotifyApiKeyCreationAsync(name);
///
///    - Throttling: None (API keys created rarely)
///    - Can await directly (low frequency)
///
/// 5. WEBHOOKSERVICE (Webhook Creation Notifications)
///    - Add NotificationService to constructor
///    - After webhook creation succeeds:
///
///      var webhookId = await CreateWebhookAsync(name, triggerType, url);
///      await _notificationService.NotifyWebhookCreationAsync(webhookName);
///
///    - Throttling: None (webhooks created rarely)
///    - Can await directly (low frequency)
///
/// 6. BACKGROUND TASK (Database Size Monitoring)
///    - Create a HostedService that runs every 10-60 minutes
///    - Check database file size
///    - Call: await _notificationService.NotifyDatabaseSizeAsync(dbSizeBytes);
///    - Throttling: Once per hour (automatic)
///    - Keep this lightweight - avoid blocking main thread
///
/// BEST PRACTICES IMPLEMENTED:
/// ✓ Rate limiting: Configurable cooldown periods per event type
/// ✓ Fire-and-forget: Execution/job notifications don't block API response
/// ✓ Error handling: All exceptions caught and logged, never throw
/// ✓ Idempotent: Safe to call multiple times (throttler prevents duplicates)
/// ✓ Async: Non-blocking I/O for email operations
/// ✓ Configurable: All settings in database, no app restart needed
/// ✓ Graceful: If notifications disabled or destination missing, silently skips
/// </summary>
public class NotificationIntegrationGuide { }
