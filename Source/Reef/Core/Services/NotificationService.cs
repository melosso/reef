// Source/Reef/Core/Services/NotificationService.cs
// Service for sending system notifications via email destinations

using Dapper;
using Microsoft.Data.Sqlite;
using Reef.Core.Destinations;
using Reef.Core.Models;
using Reef.Core.TemplateEngines;
using Scriban;
using Scriban.Runtime;
using Serilog;
using System.Text.Json;

namespace Reef.Core.Services;

/// <summary>
/// Service for sending system notifications via configured email destinations
/// All notifications are routed through EmailDestination for maximum flexibility
/// Notification settings are database-persisted with encryption
/// </summary>
public class NotificationService
{
    private readonly string _connectionString;
    private readonly HttpClient _httpClient;
    private readonly EncryptionService _encryptionService;
    private readonly NotificationThrottler _throttler;
    private readonly NotificationTemplateService _templateService;
    private readonly ScribanTemplateEngine _scribanEngine;

    // Debouncing state for email approval notifications to prevent spam when multiple approvals are created rapidly
    private static readonly SemaphoreSlim _approvalNotificationLock = new SemaphoreSlim(1, 1);
    private static CancellationTokenSource? _approvalNotificationCts;
    private static readonly TimeSpan _approvalNotificationDebounceDelay = TimeSpan.FromSeconds(10);

    public NotificationService(
        string connectionString,
        EncryptionService encryptionService,
        NotificationThrottler throttler,
        NotificationTemplateService templateService,
        ScribanTemplateEngine scribanEngine)
    {
        _connectionString = connectionString;
        _encryptionService = encryptionService;
        _throttler = throttler;
        _templateService = templateService;
        _scribanEngine = scribanEngine;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    /// <summary>
    /// Get current notification settings from database
    /// </summary>
    private async Task<NotificationSettings?> GetNotificationSettingsAsync()
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            const string sql = "SELECT * FROM NotificationSettings LIMIT 1";
            var result = await connection.QueryFirstOrDefaultAsync<NotificationSettings>(sql);
            return result;
        }
        catch (Exception ex)
        {
            Log.Error("Error retrieving notification settings from database: {Error}", ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Get destination by ID, with encrypted config
    /// </summary>
    private async Task<Destination?> GetDestinationAsync(int destinationId)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            const string sql = "SELECT * FROM Destinations WHERE Id = @Id AND IsActive = 1";
            var result = await connection.QueryFirstOrDefaultAsync<Destination>(
                sql,
                new { Id = destinationId });
            return result;
        }
        catch (Exception ex)
        {
            Log.Error("Error retrieving destination {DestinationId}: {Error}", destinationId, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Update the email approval cooldown timestamp in the database after sending notification
    /// This persists the cooldown across application restarts
    /// </summary>
    private async Task UpdateEmailApprovalCooldownTimestampAsync(int settingsId)
    {
        try
        {
            using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            const string sql = @"
                UPDATE NotificationSettings 
                SET NewEmailApprovalCooldownTimestamp = @Timestamp, UpdatedAt = @UpdatedAt
                WHERE Id = @Id";
            
            await connection.ExecuteAsync(sql, new
            {
                Id = settingsId,
                Timestamp = DateTime.UtcNow.ToString("o"),
                UpdatedAt = DateTime.UtcNow.ToString("o")
            });
            
            Log.Debug("Updated email approval cooldown timestamp for settings {SettingsId}", settingsId);
        }
        catch (Exception ex)
        {
            Log.Error("Error updating email approval cooldown timestamp: {Error}", ex.Message);
        }
    }

    /// <summary>
    /// Send notification for successful profile execution
    /// Uses throttling to prevent excessive emails (max once per 30 minutes per profile)
    /// </summary>
    public async Task SendExecutionSuccessAsync(ProfileExecution execution, Profile profile)
    {
        try
        {
            var settings = await GetNotificationSettingsAsync();
            if (settings == null || !settings.IsEnabled || !settings.NotifyOnProfileSuccess)
            {
                return;
            }

            // Check throttling - max once per 30 minutes per profile
            if (!_throttler.ShouldNotifyProfileSuccess(profile.Id))
            {
                Log.Debug("Profile success notification throttled for profile {ProfileCode} ({ProfileName})", profile.Code, profile.Name);
                return;
            }

            // Load template from database or use fallback
            var fallbackSubject = $"[Reef] Profile '{profile.Name}' executed successfully";
            var fallbackBody = BuildSuccessEmailBodyTemplate();
            var (subject, body, ctaButtonText, ctaUrlOverride) = await LoadTemplateAsync("ProfileSuccess", fallbackSubject, fallbackBody);

            // Build context with per-template CTA settings
            var context = BuildSuccessEmailContext(execution, profile, settings, ctaButtonText, ctaUrlOverride);

            // Render subject and body
            var renderedSubject = await RenderEmailTemplateAsync(subject, context);
            var renderedBody = await RenderEmailTemplateAsync(body, context);

            await SendSystemNotificationAsync(renderedSubject, renderedBody, settings);
            Log.Information("Sent success notification for execution {ExecutionId} (profile {ProfileCode} {ProfileName})",
                execution.Id, profile.Code, profile.Name);
        }
        catch (Exception ex)
        {
            Log.Error("Error sending success notification for execution {ExecutionId}: {Error}", execution.Id, ex.Message);
        }
    }

    /// <summary>
    /// Send notification for failed profile execution
    /// Uses throttling to prevent excessive emails (max once per 5 minutes per profile)
    /// </summary>
    public async Task SendExecutionFailureAsync(ProfileExecution execution, Profile profile)
    {
        try
        {
            var settings = await GetNotificationSettingsAsync();
            if (settings == null || !settings.IsEnabled || !settings.NotifyOnProfileFailure)
            {
                return;
            }

            // Check throttling - max once per 5 minutes per profile (failures are more important)
            if (!_throttler.ShouldNotifyProfileFailure(profile.Id))
            {
                Log.Debug("Profile failure notification throttled for profile {ProfileCode} ({ProfileName})", profile.Code, profile.Name);
                return;
            }

            // Load template from database or use fallback
            var fallbackSubject = $"[Reef] Profile '{profile.Name}' execution failed";
            var fallbackBody = BuildFailureEmailBodyTemplate();
            var (subject, body, ctaButtonText, ctaUrlOverride) = await LoadTemplateAsync("ProfileFailure", fallbackSubject, fallbackBody);

            // Build context with per-template CTA settings
            var context = BuildFailureEmailContext(execution, profile, settings, ctaButtonText, ctaUrlOverride);

            // Render subject and body
            var renderedSubject = await RenderEmailTemplateAsync(subject, context);
            var renderedBody = await RenderEmailTemplateAsync(body, context);

            await SendSystemNotificationAsync(renderedSubject, renderedBody, settings);
            Log.Information("Sent failure notification for execution {ExecutionId} (profile {ProfileCode} {ProfileName})",
                execution.Id, profile.Code, profile.Name);
        }
        catch (Exception ex)
        {
            Log.Error("Error sending failure notification for execution {ExecutionId}: {Error}", execution.Id, ex.Message);
        }
    }

    /// <summary>
    /// Send notification for successful import profile execution
    /// Uses throttling to prevent excessive emails (max once per 30 minutes per profile)
    /// </summary>
    public async Task SendImportExecutionSuccessAsync(ImportProfileExecution execution, ImportProfile profile)
    {
        try
        {
            var settings = await GetNotificationSettingsAsync();
            if (settings == null || !settings.IsEnabled || !settings.NotifyOnImportProfileSuccess)
            {
                return;
            }

            if (!_throttler.ShouldNotifyImportProfileSuccess(profile.Id))
            {
                Log.Debug("Import profile success notification throttled for profile {ProfileCode} ({ProfileName})", profile.Code, profile.Name);
                return;
            }

            var fallbackSubject = $"[Reef] Import '{profile.Name}' completed successfully";
            var fallbackBody = BuildImportSuccessEmailBodyTemplate();
            var (subject, body, ctaButtonText, ctaUrlOverride) = await LoadTemplateAsync("ImportProfileSuccess", fallbackSubject, fallbackBody);

            var context = BuildImportSuccessEmailContext(execution, profile, settings, ctaButtonText, ctaUrlOverride);
            var renderedSubject = await RenderEmailTemplateAsync(subject, context);
            var renderedBody = await RenderEmailTemplateAsync(body, context);

            await SendSystemNotificationAsync(renderedSubject, renderedBody, settings);
            Log.Information("Sent import success notification for execution {ExecutionId} (profile {ProfileCode} {ProfileName})",
                execution.Id, profile.Code, profile.Name);
        }
        catch (Exception ex)
        {
            Log.Error("Error sending import success notification for execution {ExecutionId}: {Error}", execution.Id, ex.Message);
        }
    }

    /// <summary>
    /// Send notification for failed import profile execution
    /// Uses throttling to prevent excessive emails (max once per 5 minutes per profile)
    /// </summary>
    public async Task SendImportExecutionFailureAsync(ImportProfileExecution execution, ImportProfile profile)
    {
        try
        {
            var settings = await GetNotificationSettingsAsync();
            if (settings == null || !settings.IsEnabled || !settings.NotifyOnImportProfileFailure)
            {
                return;
            }

            if (!_throttler.ShouldNotifyImportProfileFailure(profile.Id))
            {
                Log.Debug("Import profile failure notification throttled for profile {ProfileCode} ({ProfileName})", profile.Code, profile.Name);
                return;
            }

            var fallbackSubject = $"[Reef] Import '{profile.Name}' execution failed";
            var fallbackBody = BuildImportFailureEmailBodyTemplate();
            var (subject, body, ctaButtonText, ctaUrlOverride) = await LoadTemplateAsync("ImportProfileFailure", fallbackSubject, fallbackBody);

            var context = BuildImportFailureEmailContext(execution, profile, settings, ctaButtonText, ctaUrlOverride);
            var renderedSubject = await RenderEmailTemplateAsync(subject, context);
            var renderedBody = await RenderEmailTemplateAsync(body, context);

            await SendSystemNotificationAsync(renderedSubject, renderedBody, settings);
            Log.Information("Sent import failure notification for execution {ExecutionId} (profile {ProfileCode} {ProfileName})",
                execution.Id, profile.Code, profile.Name);
        }
        catch (Exception ex)
        {
            Log.Error("Error sending import failure notification for execution {ExecutionId}: {Error}", execution.Id, ex.Message);
        }
    }

    /// <summary>
    /// Send notification for job success
    /// Uses throttling to prevent excessive emails (max once per 30 minutes per job)
    /// </summary>
    public async Task SendJobSuccessAsync(int jobId, string jobName, Dictionary<string, object> jobDetails)
    {
        try
        {
            var settings = await GetNotificationSettingsAsync();
            if (settings == null || !settings.IsEnabled || !settings.NotifyOnJobSuccess)
            {
                return;
            }

            // Check throttling - max once per 30 minutes per job
            if (!_throttler.ShouldNotifyJobSuccess(jobId))
            {
                Log.Debug("Job success notification throttled for job {JobId}", jobId);
                return;
            }

            // Load template from database or use fallback
            var fallbackSubject = $"[Reef] Job '{jobName}' completed successfully";
            var fallbackBody = BuildJobSuccessEmailBody(jobName, jobDetails);
            var (subject, body, ctaButtonText, ctaUrlOverride) = await LoadTemplateAsync("JobSuccess", fallbackSubject, fallbackBody);

            // Build context with per-template CTA settings
            var context = BuildJobSuccessContext(jobId, jobName, jobDetails, settings, ctaButtonText, ctaUrlOverride);

            // Render subject and body
            var renderedSubject = await RenderEmailTemplateAsync(subject, context);
            var renderedBody = await RenderEmailTemplateAsync(body, context);

            await SendSystemNotificationAsync(renderedSubject, renderedBody, settings);
            Log.Information("Sent job success notification for job {JobId} ({JobName})", jobId, jobName);
        }
        catch (Exception ex)
        {
            Log.Error("Error sending job success notification for {JobName}: {Error}", jobName, ex.Message);
        }
    }

    /// <summary>
    /// Send notification for job failure
    /// Uses throttling to prevent excessive emails (max once per 5 minutes per job)
    /// </summary>
    public async Task SendJobFailureAsync(int jobId, string jobName, string errorMessage)
    {
        try
        {
            var settings = await GetNotificationSettingsAsync();
            if (settings == null || !settings.IsEnabled || !settings.NotifyOnJobFailure)
            {
                return;
            }

            // Check throttling - max once per 5 minutes per job (failures are more important)
            if (!_throttler.ShouldNotifyJobFailure(jobId))
            {
                Log.Debug("Job failure notification throttled for job {JobId}", jobId);
                return;
            }

            // Load template from database or use fallback
            var fallbackSubject = $"[Reef] Job '{jobName}' failed";
            var fallbackBody = BuildJobFailureEmailBody(jobName, errorMessage ?? "Unknown error");
            var (subject, body, ctaButtonText, ctaUrlOverride) = await LoadTemplateAsync("JobFailure", fallbackSubject, fallbackBody);

            // Build context with per-template CTA settings
            var context = BuildJobFailureContext(jobId, jobName, errorMessage ?? "Unknown error", settings, ctaButtonText, ctaUrlOverride);

            // Render subject and body
            var renderedSubject = await RenderEmailTemplateAsync(subject, context);
            var renderedBody = await RenderEmailTemplateAsync(body, context);

            await SendSystemNotificationAsync(renderedSubject, renderedBody, settings);
            Log.Information("Sent job failure notification for job {JobId} ({JobName})", jobId, jobName);
        }
        catch (Exception ex)
        {
            Log.Error("Error sending job failure notification for {JobName}: {Error}", jobName, ex.Message);
        }
    }

    /// <summary>
    /// Send notification for new user creation
    /// </summary>
    public async Task SendNewUserNotificationAsync(string username, string email)
    {
        try
        {
            var settings = await GetNotificationSettingsAsync();
            if (settings == null || !settings.IsEnabled || !settings.NotifyOnNewUser)
            {
                return;
            }

            // Load template from database or use fallback
            var fallbackSubject = $"[Reef] New user created: {username}";
            var fallbackBody = BuildNewUserEmailBody(username, email ?? "N/A");
            var (subject, body, ctaButtonText, ctaUrlOverride) = await LoadTemplateAsync("NewUser", fallbackSubject, fallbackBody);

            // Build context with per-template CTA settings
            var context = BuildNewUserContext(username, email ?? "N/A", settings, ctaButtonText, ctaUrlOverride);

            // Render subject and body
            var renderedSubject = await RenderEmailTemplateAsync(subject, context);
            var renderedBody = await RenderEmailTemplateAsync(body, context);

            await SendSystemNotificationAsync(renderedSubject, renderedBody, settings);
            Log.Information("Sent new user notification for {Username}", username);
        }
        catch (Exception ex)
        {
            Log.Error("Error sending new user notification for {Username}: {Error}", username, ex.Message);
        }
    }

    /// <summary>
    /// Send notification for new API key
    /// </summary>
    public async Task SendNewApiKeyNotificationAsync(string keyName)
    {
        try
        {
            var settings = await GetNotificationSettingsAsync();
            if (settings == null || !settings.IsEnabled || !settings.NotifyOnNewApiKey)
            {
                return;
            }

            // Load template from database or use fallback
            var fallbackSubject = $"[Reef] New API key created: {keyName}";
            var fallbackBody = BuildNewApiKeyEmailBody(keyName);
            var (subject, body, ctaButtonText, ctaUrlOverride) = await LoadTemplateAsync("NewApiKey", fallbackSubject, fallbackBody);

            // Build context with per-template CTA settings
            var context = BuildNewApiKeyContext(keyName, settings, ctaButtonText, ctaUrlOverride);

            // Render subject and body
            var renderedSubject = await RenderEmailTemplateAsync(subject, context);
            var renderedBody = await RenderEmailTemplateAsync(body, context);

            await SendSystemNotificationAsync(renderedSubject, renderedBody, settings);
            Log.Information("Sent new API key notification for {KeyName}", keyName);
        }
        catch (Exception ex)
        {
            Log.Error("Error sending new API key notification for {KeyName}: {Error}", keyName, ex.Message);
        }
    }

    /// <summary>
    /// Send notification for new webhook
    /// </summary>
    public async Task SendNewWebhookNotificationAsync(string webhookName)
    {
        try
        {
            var settings = await GetNotificationSettingsAsync();
            if (settings == null || !settings.IsEnabled || !settings.NotifyOnNewWebhook)
            {
                return;
            }

            // Load template from database or use fallback
            var fallbackSubject = $"[Reef] New webhook created: {webhookName}";
            var fallbackBody = BuildNewWebhookEmailBody(webhookName);
            var (subject, body, ctaButtonText, ctaUrlOverride) = await LoadTemplateAsync("NewWebhook", fallbackSubject, fallbackBody);

            // Build context with per-template CTA settings
            var context = BuildNewWebhookContext(webhookName, settings, ctaButtonText, ctaUrlOverride);

            // Render subject and body
            var renderedSubject = await RenderEmailTemplateAsync(subject, context);
            var renderedBody = await RenderEmailTemplateAsync(body, context);

            await SendSystemNotificationAsync(renderedSubject, renderedBody, settings);
            Log.Information("Sent new webhook notification for {WebhookName}", webhookName);
        }
        catch (Exception ex)
        {
            Log.Error("Error sending new webhook notification for {WebhookName}: {Error}", webhookName, ex.Message);
        }
    }

    /// <summary>
    /// Queue an email approval notification with debouncing to prevent spam from rapid batch imports
    /// This method implements a 10-second debounce window - if multiple approvals are created within 10 seconds,
    /// only ONE notification is sent after the delay with the current total count
    /// </summary>
    public async Task QueueEmailApprovalNotificationAsync()
    {
        await _approvalNotificationLock.WaitAsync();
        try
        {
            // Cancel any existing pending notification
            _approvalNotificationCts?.Cancel();
            _approvalNotificationCts?.Dispose();

            // Create a new cancellation token for this debounce window
            _approvalNotificationCts = new CancellationTokenSource();
            var cts = _approvalNotificationCts;

            Log.Debug("Email approval notification queued with {Delay}s debounce",
                _approvalNotificationDebounceDelay.TotalSeconds);

            // Start debounce delay (fire and forget)
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(_approvalNotificationDebounceDelay, cts.Token);

                    // After delay, get the current pending count and check if notification should be sent
                    using var connection = new SqliteConnection(_connectionString);
                    await connection.OpenAsync();

                    var pendingCount = await connection.ExecuteScalarAsync<int>(
                        "SELECT COUNT(*) FROM PendingEmailApprovals WHERE Status = 'Pending'");

                    Log.Debug("Debounce delay completed, evaluating notification for {Count} pending approval(s)",
                        pendingCount);

                    await SendNewEmailApprovalNotificationAsync(pendingCount);
                }
                catch (TaskCanceledException)
                {
                    Log.Debug("Email approval notification was cancelled (another approval came in)");
                }
                catch (Exception ex)
                {
                    Log.Error("Error sending debounced email approval notification: {Error}", ex.Message);
                }
            });
        }
        finally
        {
            _approvalNotificationLock.Release();
        }
    }

    /// <summary>
    /// Send notification for new email approval items
    /// Uses database-persisted throttling to prevent excessive emails (cooldown configurable, default once per 24 hours)
    /// This ensures cooldown persists across application restarts
    /// </summary>
    public async Task SendNewEmailApprovalNotificationAsync(int pendingCount)
    {
        try
        {
            if (pendingCount == 0)
            {
                Log.Debug("Skipping email approval notification because there are no pending items.");
                return;
            }
            
            var settings = await GetNotificationSettingsAsync();
            if (settings == null || !settings.IsEnabled || !settings.NotifyOnNewEmailApproval)
            {
                return;
            }

            // Check database-persisted cooldown timestamp (survives app restarts)
            if (settings.NewEmailApprovalCooldownTimestamp.HasValue)
            {
                var timeSinceLastNotification = DateTime.UtcNow - settings.NewEmailApprovalCooldownTimestamp.Value;
                var cooldownPeriod = TimeSpan.FromHours(settings.NewEmailApprovalCooldownHours);

                if (timeSinceLastNotification < cooldownPeriod)
                {
                    var remainingTime = cooldownPeriod - timeSinceLastNotification;
                    Log.Debug("Email approval notification skipped due to cooldown period (last sent: {TimeSince} ago, cooldown: {Hours}h, remaining: {RemainingHours}h {RemainingMinutes}m)",
                        timeSinceLastNotification.ToString(@"hh\:mm\:ss"),
                        settings.NewEmailApprovalCooldownHours,
                        (int)remainingTime.TotalHours,
                        remainingTime.Minutes);
                    return;
                }
            }

            // Cooldown check passed - proceed with sending notification
            Log.Information("Sending email approval notification for {Count} pending approval(s) (cooldown check passed)",
                pendingCount);

            // Load template from database or use fallback
            var fallbackSubject = $"[Reef] {pendingCount} email{(pendingCount != 1 ? "s" : "")} pending approval";
            var fallbackBody = BuildNewEmailApprovalEmailBody(pendingCount);
            var (subject, body, ctaButtonText, ctaUrlOverride) = await LoadTemplateAsync("NewEmailApproval", fallbackSubject, fallbackBody);

            // Build context with per-template CTA settings
            var context = BuildEmailApprovalContext(pendingCount, settings, ctaButtonText, ctaUrlOverride);

            // Render subject and body
            var renderedSubject = await RenderEmailTemplateAsync(subject, context);
            var renderedBody = await RenderEmailTemplateAsync(body, context);

            await SendSystemNotificationAsync(renderedSubject, renderedBody, settings);

            // Update cooldown timestamp in database after successful send
            await UpdateEmailApprovalCooldownTimestampAsync(settings.Id);

            Log.Information("Email approval notification sent successfully (next notification allowed in {Hours}h)",
                settings.NewEmailApprovalCooldownHours);
        }
        catch (Exception ex)
        {
            Log.Error("Error sending new email approval notification: {Error}", ex.Message);
        }
    }

    /// <summary>
    /// Send notification for database size threshold exceeded
    /// Uses throttling to prevent excessive emails (max once per hour)
    /// </summary>
    public async Task SendDatabaseSizeNotificationAsync(long currentSizeBytes)
    {
        try
        {
            var settings = await GetNotificationSettingsAsync();
            if (settings == null || !settings.IsEnabled || !settings.NotifyOnDatabaseSizeThreshold)
            {
                return;
            }

            if (currentSizeBytes <= settings.DatabaseSizeThresholdBytes)
            {
                return; // Size is under threshold
            }

            // Check throttling - max once per hour (expensive check, no need for frequent alerts)
            if (!_throttler.ShouldNotifyDatabaseSize())
            {
                Log.Debug("Database size notification throttled");
                return;
            }

            var thresholdMb = settings.DatabaseSizeThresholdBytes / (1024 * 1024);
            var currentMb = currentSizeBytes / (1024 * 1024);
            var excessMb = currentMb - thresholdMb;

            // Load template from database or use fallback
            var fallbackSubject = $"[Reef] Database size critical: {currentMb}MB (threshold: {thresholdMb}MB)";
            var fallbackBody = BuildDatabaseSizeEmailBody(thresholdMb, currentMb, excessMb);
            var (subject, body, ctaButtonText, ctaUrlOverride) = await LoadTemplateAsync("DatabaseSizeThreshold", fallbackSubject, fallbackBody);

            // Build context with per-template CTA settings
            var context = BuildDatabaseSizeContext(thresholdMb, currentMb, excessMb, settings, ctaButtonText, ctaUrlOverride);

            // Render subject and body
            var renderedSubject = await RenderEmailTemplateAsync(subject, context);
            var renderedBody = await RenderEmailTemplateAsync(body, context);

            await SendSystemNotificationAsync(renderedSubject, renderedBody, settings);
            Log.Information("Sent database size threshold notification ({CurrentMb}MB > {ThresholdMb}MB)",
                currentMb, thresholdMb);
        }
        catch (Exception ex)
        {
            Log.Error("Error sending database size notification: {Error}", ex.Message);
        }
    }

    /// <summary>
    /// Core method to send system notification via EmailDestination
    /// </summary>
    private async Task SendSystemNotificationAsync(string subject, string body, NotificationSettings settings)
    {
        try
        {
            var destination = await GetDestinationAsync(settings.DestinationId);
            if (destination == null)
            {
                Log.Warning("Notification destination {DestinationId} not found or inactive", settings.DestinationId);
                return;
            }

            // Decrypt the destination configuration
            var decryptedConfig = destination.ConfigurationJson;

            // Check if configuration is encrypted before attempting decryption
            if (_encryptionService.IsEncrypted(decryptedConfig))
            {
                decryptedConfig = _encryptionService.Decrypt(decryptedConfig);
            }

            // Parse the email destination config
            var emailConfig = JsonSerializer.Deserialize<EmailDestinationConfiguration>(decryptedConfig);
            if (emailConfig == null)
            {
                Log.Warning("Failed to deserialize email destination configuration");
                return;
            }

            // Override recipients if specified in notification settings
            if (!string.IsNullOrWhiteSpace(settings.RecipientEmails))
            {
                emailConfig.ToAddresses = settings.RecipientEmails
                    .Split(';')
                    .Select(e => e.Trim())
                    .Where(e => !string.IsNullOrWhiteSpace(e))
                    .ToList();
            }

            // Validate that we have recipients
            if (emailConfig.ToAddresses == null || emailConfig.ToAddresses.Count == 0)
            {
                Log.Warning("No recipient addresses configured for notification destination");
                return;
            }

            // Properly construct the MimeMessage with all required fields
            var message = new MimeKit.MimeMessage();
            message.From.Add(new MimeKit.MailboxAddress(
                emailConfig.FromName ?? "Reef",
                emailConfig.FromAddress));

            // Add recipients
            foreach (var recipient in emailConfig.ToAddresses)
            {
                if (!string.IsNullOrWhiteSpace(recipient))
                {
                    try
                    {
                        message.To.Add(MimeKit.MailboxAddress.Parse(recipient.Trim()));
                    }
                    catch (Exception ex)
                    {
                        Log.Warning("Invalid recipient email address: {Email}: {Error}", recipient, ex.Message);
                    }
                }
            }

            // Set subject and body using BodyBuilder (matching EmailDestination pattern)
            message.Subject = subject;
            var bodyBuilder = new MimeKit.BodyBuilder
            {
                HtmlBody = body
            };
            message.Body = bodyBuilder.ToMessageBody();

            // Detect email provider (with fallback detection for backward compatibility)
            string providerType = emailConfig.EmailProvider?.ToLower() ?? "";

            // If EmailProvider is not set, detect from configuration fields
            if (string.IsNullOrEmpty(providerType))
            {
                if (!string.IsNullOrEmpty(emailConfig.ResendApiKey))
                {
                    providerType = "resend";
                }
                else if (!string.IsNullOrEmpty(emailConfig.SendGridApiKey))
                {
                    providerType = "sendgrid";
                }
                else
                {
                    providerType = "smtp";
                }
            }

            // Route to appropriate provider based on configuration
            IEmailProvider emailProvider = providerType switch
            {
                "resend" => new ResendEmailProvider(),
                "sendgrid" => new SendGridEmailProvider(),
                _ => new SmtpEmailProvider() // Default to SMTP
            };

            var result = await emailProvider.SendEmailAsync(message, emailConfig);

            if (!result.Success)
            {
                Log.Warning("Failed to send notification: {Error}", result.ErrorMessage);
            }
            else
            {
                Log.Information("System notification sent successfully: {Subject} to {RecipientCount} recipients",
                    subject, emailConfig.ToAddresses.Count);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Error sending system notification: {Error}", ex.Message);
        }
    }

    // ===== Email Body Builders =====

    private Dictionary<string, string> BuildSuccessEmailPlaceholders(ProfileExecution execution, Profile profile)
    {
        var executionTime = execution.ExecutionTimeMs.HasValue
            ? $"{execution.ExecutionTimeMs / 1000.0:F2} seconds"
            : "Unknown";

        var fileSize = execution.FileSizeBytes.HasValue
            ? FormatFileSize(execution.FileSizeBytes.Value)
            : "Unknown";

        return new Dictionary<string, string>
        {
            { "ProfileName", profile.Name },
            { "ExecutionId", execution.Id.ToString() },
            { "StartedAt", execution.StartedAt.ToString("o") }, // ISO 8601 format for DateTime parsing
            { "CompletedAt", execution.CompletedAt?.ToString("o") ?? DateTime.UtcNow.ToString("o") },
            { "ExecutionTime", executionTime },
            { "RowCount", execution.RowCount?.ToString("N0") ?? "0" },
            { "FileSize", fileSize },
            { "OutputPath", execution.OutputPath ?? "N/A" }
        };
    }

    private string BuildSuccessEmailBodyTemplate()
    {
        return @"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <style>
        * { margin: 0; padding: 0; }
        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif; line-height: 1.6; color: #333; background-color: #f5f5f5; }
        .container { max-width: 600px; margin: 0 auto; padding: 10px; }
        .card { background-color: white; border-radius: 8px; overflow: hidden; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }
        .header { background-color: #10b981; color: white; padding: 30px 20px; text-align: center; }
        .header h2 { font-size: 24px; margin: 0; font-weight: 600; }
        .content { padding: 20px; }
        .section { margin: 20px 0; }
        .detail-row { display: flex; padding: 12px 0; border-bottom: 1px solid #f0f0f0; word-break: break-word; }
        .detail-row:last-child { border-bottom: none; }
        .label { font-weight: 600; color: #10b981; min-width: 140px; padding-right: 15px; }
        .value { color: #555; flex: 1; word-break: break-all; }
        @media (max-width: 600px) {
            .container { padding: 5px; }
            .content { padding: 15px; }
            .detail-row { flex-direction: column; }
            .label { min-width: 100%; margin-bottom: 5px; }
            .header h2 { font-size: 20px; }
        }
    </style>
</head>
<body>
    <div class='container'>
        <div class='card'>
            <div class='header'>
                <h2>✓ Profile Executed Successfully</h2>
            </div>
            <div class='content'>
                <div class='section'>
                    <div class='detail-row'>
                        <span class='label'>Profile:</span>
                        <span class='value'>{ProfileName}</span>
                    </div>
                    <div class='detail-row'>
                        <span class='label'>Execution ID:</span>
                        <span class='value'>{ExecutionId}</span>
                    </div>
                    <div class='detail-row'>
                        <span class='label'>Started At:</span>
                        <span class='value'>{StartedAt.GMT+1}</span>
                    </div>
                    <div class='detail-row'>
                        <span class='label'>Completed At:</span>
                        <span class='value'>{CompletedAt.GMT+1}</span>
                    </div>
                    <div class='detail-row'>
                        <span class='label'>Execution Time:</span>
                        <span class='value'>{ExecutionTime}</span>
                    </div>
                    <div class='detail-row'>
                        <span class='label'>Rows Exported:</span>
                        <span class='value'>{RowCount}</span>
                    </div>
                    <div class='detail-row'>
                        <span class='label'>File Size:</span>
                        <span class='value'>{FileSize}</span>
                    </div>
                    <div class='detail-row'>
                        <span class='label'>Output Path:</span>
                        <span class='value'>{OutputPath}</span>
                    </div>
                </div>
            </div>
        </div>
    </div>
</body>
</html>";
    }

    private Dictionary<string, string> BuildFailureEmailPlaceholders(ProfileExecution execution, Profile profile)
    {
        return new Dictionary<string, string>
        {
            { "ProfileName", profile.Name },
            { "ExecutionId", execution.Id.ToString() },
            { "StartedAt", execution.StartedAt.ToString("o") },
            { "CompletedAt", execution.CompletedAt?.ToString("o") ?? DateTime.UtcNow.ToString("o") },
            { "ErrorMessage", execution.ErrorMessage ?? "No error message available" }
        };
    }

    private string BuildFailureEmailBodyTemplate()
    {
        return @"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <style>
        * { margin: 0; padding: 0; }
        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif; line-height: 1.6; color: #333; background-color: #f5f5f5; }
        .container { max-width: 600px; margin: 0 auto; padding: 10px; }
        .card { background-color: white; border-radius: 8px; overflow: hidden; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }
        .header { background-color: #ef4444; color: white; padding: 30px 20px; text-align: center; }
        .header h2 { font-size: 24px; margin: 0; font-weight: 600; }
        .content { padding: 20px; }
        .section { margin: 20px 0; }
        .detail-row { display: flex; padding: 12px 0; border-bottom: 1px solid #f0f0f0; word-break: break-word; }
        .detail-row:last-child { border-bottom: none; }
        .label { font-weight: 600; color: #ef4444; min-width: 140px; padding-right: 15px; }
        .value { color: #555; flex: 1; word-break: break-all; }
        .error-box { background-color: #fee2e2; border-left: 5px solid #ef4444; padding: 15px; margin: 20px 0; border-radius: 4px; }
        .error-box strong { color: #991b1b; display: block; margin-bottom: 8px; }
        .error-message { color: #7f1d1d; word-break: break-word; font-family: 'Courier New', monospace; font-size: 13px; line-height: 1.5; }
        @media (max-width: 600px) {
            .container { padding: 5px; }
            .content { padding: 15px; }
            .detail-row { flex-direction: column; }
            .label { min-width: 100%; margin-bottom: 5px; }
            .header h2 { font-size: 20px; }
            .error-box { padding: 12px; }
        }
    </style>
</head>
<body>
    <div class='container'>
        <div class='card'>
            <div class='header'>
                <h2>Profile Execution Failed</h2>
            </div>
            <div class='content'>
                <div class='section'>
                    <div class='detail-row'>
                        <span class='label'>Profile:</span>
                        <span class='value'>{ProfileName}</span>
                    </div>
                    <div class='detail-row'>
                        <span class='label'>Execution ID:</span>
                        <span class='value'>{ExecutionId}</span>
                    </div>
                    <div class='detail-row'>
                        <span class='label'>Started At:</span>
                        <span class='value'>{StartedAt.GMT+1}</span>
                    </div>
                    <div class='detail-row'>
                        <span class='label'>Failed At:</span>
                        <span class='value'>{CompletedAt.GMT+1}</span>
                    </div>
                </div>
                <div class='error-box'>
                    <strong>Error Details:</strong>
                    <div class='error-message'>{ErrorMessage}</div>
                </div>
                <p style='color: #666; font-size: 13px;'>Please check the Reef dashboard execution logs for additional diagnostic information.</p>
            </div>
        </div>
    </div>
</body>
</html>";
    }

    private static string BuildImportSuccessEmailBodyTemplate() =>
        "<p>Import profile <strong>{{ ProfileName }}</strong> completed successfully. " +
        "Execution ID: {{ ExecutionId }}. Rows imported: {{ RowsImported }}. Rows failed: {{ RowsFailed }}.</p>";

    private static string BuildImportFailureEmailBodyTemplate() =>
        "<p>Import profile <strong>{{ ProfileName }}</strong> failed. " +
        "Execution ID: {{ ExecutionId }}. Error: {{ ErrorMessage }}</p>";

    private string BuildJobSuccessEmailBody(string jobName, Dictionary<string, object> jobDetails)
    {
        var detailsHtml = string.Join("",
            jobDetails.Select(kvp => $@"
                    <div class='detail-row'>
                        <span class='label'>{kvp.Key}:</span>
                        <span class='value'>{kvp.Value}</span>
                    </div>"));

        return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <style>
        * {{ margin: 0; padding: 0; }}
        body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif; line-height: 1.6; color: #333; background-color: #f5f5f5; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 10px; }}
        .card {{ background-color: white; border-radius: 8px; overflow: hidden; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }}
        .header {{ background-color: #10b981; color: white; padding: 30px 20px; text-align: center; }}
        .header h2 {{ font-size: 24px; margin: 0; font-weight: 600; }}
        .content {{ padding: 20px; }}
        .section {{ margin: 20px 0; }}
        .detail-row {{ display: flex; padding: 12px 0; border-bottom: 1px solid #f0f0f0; word-break: break-word; }}
        .detail-row:last-child {{ border-bottom: none; }}
        .label {{ font-weight: 600; color: #10b981; min-width: 140px; padding-right: 15px; }}
        .value {{ color: #555; flex: 1; word-break: break-all; }}
{GetCtaStyles()}
        @media (max-width: 600px) {{
            .container {{ padding: 5px; }}
            .content {{ padding: 15px; }}
            .detail-row {{ flex-direction: column; }}
            .label {{ min-width: 100%; margin-bottom: 5px; }}
            .header h2 {{ font-size: 20px; }}
        }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='card'>
            <div class='header'>
                <h2>✓ Job Completed Successfully</h2>
            </div>
            <div class='content'>
                <div class='section'>
                    <div class='detail-row'>
                        <span class='label'>Job:</span>
                        <span class='value'>{jobName}</span>
                    </div>
                    {detailsHtml}
                    <div class='detail-row'>
                        <span class='label'>Completed At:</span>
                        <span class='value'>{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</span>
                    </div>
                </div>
{GetCtaFooterHtml()}
            </div>
        </div>
    </div>
</body>
</html>";
    }

    private string BuildJobFailureEmailBody(string jobName, string errorMessage)
    {
        return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <style>
        * {{ margin: 0; padding: 0; }}
        body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif; line-height: 1.6; color: #333; background-color: #f5f5f5; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 10px; }}
        .card {{ background-color: white; border-radius: 8px; overflow: hidden; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }}
        .header {{ background-color: #ef4444; color: white; padding: 30px 20px; text-align: center; }}
        .header h2 {{ font-size: 24px; margin: 0; font-weight: 600; }}
        .content {{ padding: 20px; }}
        .section {{ margin: 20px 0; }}
        .detail-row {{ display: flex; padding: 12px 0; border-bottom: 1px solid #f0f0f0; word-break: break-word; }}
        .detail-row:last-child {{ border-bottom: none; }}
        .label {{ font-weight: 600; color: #ef4444; min-width: 140px; padding-right: 15px; }}
        .value {{ color: #555; flex: 1; word-break: break-all; }}
        .error-box {{ background-color: #fee2e2; border-left: 5px solid #ef4444; padding: 15px; margin: 20px 0; border-radius: 4px; }}
        .error-box strong {{ color: #991b1b; display: block; margin-bottom: 8px; }}
        .error-message {{ color: #7f1d1d; word-break: break-word; font-family: 'Courier New', monospace; font-size: 13px; line-height: 1.5; }}
{GetCtaStyles()}
        @media (max-width: 600px) {{
            .container {{ padding: 5px; }}
            .content {{ padding: 15px; }}
            .detail-row {{ flex-direction: column; }}
            .label {{ min-width: 100%; margin-bottom: 5px; }}
            .header h2 {{ font-size: 20px; }}
            .error-box {{ padding: 12px; }}
        }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='card'>
            <div class='header'>
                <h2>Job Failed</h2>
            </div>
            <div class='content'>
                <div class='section'>
                    <div class='detail-row'>
                        <span class='label'>Job:</span>
                        <span class='value'>{jobName}</span>
                    </div>
                    <div class='detail-row'>
                        <span class='label'>Failed At:</span>
                        <span class='value'>{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</span>
                    </div>
                </div>
                <div class='error-box'>
                    <strong>Error Details:</strong>
                    <div class='error-message'>{errorMessage}</div>
                </div>
                <p style='color: #666; font-size: 13px;'>Please check the Reef dashboard job logs for additional diagnostic information.</p>
{GetCtaFooterHtml()}
            </div>
        </div>
    </div>
</body>
</html>";
    }

    private string BuildNewUserEmailBody(string username, string email)
    {
        return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <style>
        * {{ margin: 0; padding: 0; }}
        body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif; line-height: 1.6; color: #333; background-color: #f5f5f5; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 10px; }}
        .card {{ background-color: white; border-radius: 8px; overflow: hidden; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }}
        .header {{ background-color: #3b82f6; color: white; padding: 30px 20px; text-align: center; }}
        .header h2 {{ font-size: 24px; margin: 0; font-weight: 600; }}
        .content {{ padding: 20px; }}
        .section {{ margin: 20px 0; }}
        .detail-row {{ display: flex; padding: 12px 0; border-bottom: 1px solid #f0f0f0; word-break: break-word; }}
        .detail-row:last-child {{ border-bottom: none; }}
        .label {{ font-weight: 600; color: #3b82f6; min-width: 140px; padding-right: 15px; }}
        .value {{ color: #555; flex: 1; word-break: break-all; }}
{GetCtaStyles()}
        @media (max-width: 600px) {{
            .container {{ padding: 5px; }}
            .content {{ padding: 15px; }}
            .detail-row {{ flex-direction: column; }}
            .label {{ min-width: 100%; margin-bottom: 5px; }}
            .header h2 {{ font-size: 20px; }}
        }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='card'>
            <div class='header'>
                <h2>👤 New User Created</h2>
            </div>
            <div class='content'>
                <div class='section'>
                    <div class='detail-row'>
                        <span class='label'>Username:</span>
                        <span class='value'>{username}</span>
                    </div>
                    <div class='detail-row'>
                        <span class='label'>Email:</span>
                        <span class='value'>{email}</span>
                    </div>
                    <div class='detail-row'>
                        <span class='label'>Created At:</span>
                        <span class='value'>{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</span>
                    </div>
                </div>
{GetCtaFooterHtml()}
            </div>
        </div>
    </div>
</body>
</html>";
    }

    private string BuildNewApiKeyEmailBody(string keyName)
    {
        return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <style>
        * {{ margin: 0; padding: 0; }}
        body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif; line-height: 1.6; color: #333; background-color: #f5f5f5; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 10px; }}
        .card {{ background-color: white; border-radius: 8px; overflow: hidden; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }}
        .header {{ background-color: #8b5cf6; color: white; padding: 30px 20px; text-align: center; }}
        .header h2 {{ font-size: 24px; margin: 0; font-weight: 600; }}
        .content {{ padding: 20px; }}
        .section {{ margin: 20px 0; }}
        .detail-row {{ display: flex; padding: 12px 0; border-bottom: 1px solid #f0f0f0; word-break: break-word; }}
        .detail-row:last-child {{ border-bottom: none; }}
        .label {{ font-weight: 600; color: #8b5cf6; min-width: 140px; padding-right: 15px; }}
        .value {{ color: #555; flex: 1; word-break: break-all; }}
{GetCtaStyles()}
        @media (max-width: 600px) {{
            .container {{ padding: 5px; }}
            .content {{ padding: 15px; }}
            .detail-row {{ flex-direction: column; }}
            .label {{ min-width: 100%; margin-bottom: 5px; }}
            .header h2 {{ font-size: 20px; }}
        }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='card'>
            <div class='header'>
                <h2>New API Key Created</h2>
            </div>
            <div class='content'>
                <div class='section'>
                    <div class='detail-row'>
                        <span class='label'>Key Name:</span>
                        <span class='value'>{keyName}</span>
                    </div>
                    <div class='detail-row'>
                        <span class='label'>Created At:</span>
                        <span class='value'>{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</span>
                    </div>
                </div>
{GetCtaFooterHtml()}
            </div>
        </div>
    </div>
</body>
</html>";
    }

    private string BuildNewWebhookEmailBody(string webhookName)
    {
        return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <style>
        * {{ margin: 0; padding: 0; }}
        body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif; line-height: 1.6; color: #333; background-color: #f5f5f5; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 10px; }}
        .card {{ background-color: white; border-radius: 8px; overflow: hidden; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }}
        .header {{ background-color: #06b6d4; color: white; padding: 30px 20px; text-align: center; }}
        .header h2 {{ font-size: 24px; margin: 0; font-weight: 600; }}
        .content {{ padding: 20px; }}
        .section {{ margin: 20px 0; }}
        .detail-row {{ display: flex; padding: 12px 0; border-bottom: 1px solid #f0f0f0; word-break: break-word; }}
        .detail-row:last-child {{ border-bottom: none; }}
        .label {{ font-weight: 600; color: #06b6d4; min-width: 140px; padding-right: 15px; }}
        .value {{ color: #555; flex: 1; word-break: break-all; }}
{GetCtaStyles()}
        @media (max-width: 600px) {{
            .container {{ padding: 5px; }}
            .content {{ padding: 15px; }}
            .detail-row {{ flex-direction: column; }}
            .label {{ min-width: 100%; margin-bottom: 5px; }}
            .header h2 {{ font-size: 20px; }}
        }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='card'>
            <div class='header'>
                <h2>🪝 New Webhook Created</h2>
            </div>
            <div class='content'>
                <div class='section'>
                    <div class='detail-row'>
                        <span class='label'>Webhook:</span>
                        <span class='value'>{webhookName}</span>
                    </div>
                    <div class='detail-row'>
                        <span class='label'>Created At:</span>
                        <span class='value'>{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</span>
                    </div>
                </div>
{GetCtaFooterHtml()}
            </div>
        </div>
    </div>
</body>
</html>";
    }

    private string BuildNewEmailApprovalEmailBody(int pendingCount)
    {
        return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <style>
        * {{ margin: 0; padding: 0; }}
        body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif; line-height: 1.6; color: #333; background-color: #f5f5f5; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 10px; }}
        .card {{ background-color: white; border-radius: 8px; overflow: hidden; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }}
        .header {{ background-color: #f59e0b; color: white; padding: 30px 20px; text-align: center; }}
        .header h2 {{ font-size: 24px; margin: 0; font-weight: 600; }}
        .content {{ padding: 20px; }}
        .section {{ margin: 20px 0; }}
        .detail-row {{ display: flex; padding: 12px 0; border-bottom: 1px solid #f0f0f0; word-break: break-word; }}
        .detail-row:last-child {{ border-bottom: none; }}
        .label {{ font-weight: 600; color: #f59e0b; min-width: 140px; padding-right: 15px; }}
        .value {{ color: #555; flex: 1; word-break: break-all; }}
        .info-box {{ background-color: #fef3c7; border-left: 5px solid #f59e0b; padding: 15px; margin: 20px 0; border-radius: 4px; }}
        .info-box p {{ color: #92400e; margin: 0; }}
{GetCtaStyles()}
        @media (max-width: 600px) {{
            .container {{ padding: 5px; }}
            .content {{ padding: 15px; }}
            .detail-row {{ flex-direction: column; }}
            .label {{ min-width: 100%; margin-bottom: 5px; }}
            .header h2 {{ font-size: 20px; }}
            .info-box {{ padding: 12px; }}
        }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='card'>
            <div class='header'>
                <h2>✉️ Email{(pendingCount != 1 ? "s" : "")} Pending Approval</h2>
            </div>
            <div class='content'>
                <div class='section'>
                    <div class='detail-row'>
                        <span class='label'>Pending Items:</span>
                        <span class='value'>{pendingCount}</span>
                    </div>
                    <div class='detail-row'>
                        <span class='label'>Notification Time:</span>
                        <span class='value'>{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</span>
                    </div>
                </div>
                <div class='info-box'>
                    <p>There {(pendingCount != 1 ? "are" : "is")} {pendingCount} email{(pendingCount != 1 ? "s" : "")} waiting for approval in the workflow. Please review and approve or reject {(pendingCount != 1 ? "them" : "it")} in the Reef dashboard.</p>
                </div>
{GetCtaFooterHtml()}
            </div>
        </div>
    </div>
</body>
</html>";
    }

    private string BuildDatabaseSizeEmailBody(long thresholdMb, long currentMb, long excessMb)
    {
        return $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <style>
        * {{ margin: 0; padding: 0; }}
        body {{ font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, 'Helvetica Neue', Arial, sans-serif; line-height: 1.6; color: #333; background-color: #f5f5f5; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 10px; }}
        .card {{ background-color: white; border-radius: 8px; overflow: hidden; box-shadow: 0 2px 4px rgba(0,0,0,0.1); }}
        .header {{ background-color: #f59e0b; color: white; padding: 30px 20px; text-align: center; }}
        .header h2 {{ font-size: 24px; margin: 0; font-weight: 600; }}
        .content {{ padding: 20px; }}
        .section {{ margin: 20px 0; }}
        .detail-row {{ display: flex; padding: 12px 0; border-bottom: 1px solid #f0f0f0; word-break: break-word; }}
        .detail-row:last-child {{ border-bottom: none; }}
        .label {{ font-weight: 600; color: #f59e0b; min-width: 140px; padding-right: 15px; }}
        .value {{ color: #555; flex: 1; word-break: break-all; }}
        .warning {{ background-color: #fef3c7; border-left: 5px solid #f59e0b; padding: 15px; margin: 20px 0; border-radius: 4px; }}
        .warning p {{ color: #92400e; margin: 0; }}
{GetCtaStyles()}
        @media (max-width: 600px) {{
            .container {{ padding: 5px; }}
            .content {{ padding: 15px; }}
            .detail-row {{ flex-direction: column; }}
            .label {{ min-width: 100%; margin-bottom: 5px; }}
            .header h2 {{ font-size: 20px; }}
            .warning {{ padding: 12px; }}
        }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='card'>
            <div class='header'>
                <h2>⚠️ Database Size Alert</h2>
            </div>
            <div class='content'>
                <div class='section'>
                    <div class='detail-row'>
                        <span class='label'>Threshold:</span>
                        <span class='value'>{thresholdMb} MB</span>
                    </div>
                    <div class='detail-row'>
                        <span class='label'>Current Size:</span>
                        <span class='value'>{currentMb} MB</span>
                    </div>
                    <div class='detail-row'>
                        <span class='label'>Over Threshold:</span>
                        <span class='value'>{excessMb} MB (+{((double)excessMb / thresholdMb * 100):F1}%)</span>
                    </div>
                    <div class='detail-row'>
                        <span class='label'>Checked At:</span>
                        <span class='value'>{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC</span>
                    </div>
                </div>
                <div class='warning'>
                    <p>Please review your retention policies and consider archiving or cleaning old execution records to reclaim database storage space.</p>
                </div>
{GetCtaFooterHtml()}
            </div>
        </div>
    </div>
</body>
</html>";
    }

    private string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len = len / 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    /// <summary>
    /// Get CTA footer HTML with Scriban templating for conditional rendering
    /// </summary>
    private string GetCtaFooterHtml()
    {
        return @"
                {{~ if EnableCTA && CTAUrl != """" ~}}
                <div class='cta-section'>
                    <a href='{{ CTAUrl }}' class='cta-button'>{{ CTAButtonText }}</a>
                </div>
                {{~ end ~}}";
    }

    /// <summary>
    /// Get CTA CSS styles for email templates
    /// </summary>
    private string GetCtaStyles()
    {
        return @"
        .cta-section { text-align: center; padding: 25px 20px; border-top: 1px solid #e5e7eb; margin-top: 20px; }
        .cta-button { display: inline-block; padding: 12px 30px; background-color: #3b82f6; color: white; text-decoration: none; border-radius: 6px; font-weight: 600; font-size: 14px; transition: background-color 0.2s; }
        .cta-button:hover { background-color: #2563eb; }
        @media (max-width: 600px) {
            .cta-section { padding: 20px 15px; }
            .cta-button { display: block; width: 100%; }
        }";
    }



    /// <summary>
    /// Render email template using Scriban template engine
    /// </summary>
    private async Task<string> RenderEmailTemplateAsync(string template, ScriptObject context)
    {
        try
        {
            var scribanTemplate = Template.Parse(template);

            if (scribanTemplate.HasErrors)
            {
                var errors = string.Join("; ", scribanTemplate.Messages.Select(m => m.Message));
                Log.Warning("Template parsing error: {Errors}", errors);
                return template; // Return original if parsing fails
            }

            var templateContext = new TemplateContext
            {
                StrictVariables = false,
                EnableRelaxedMemberAccess = true
            };

            templateContext.PushGlobal(context);

            var rendered = await scribanTemplate.RenderAsync(templateContext);
            return rendered;
        }
        catch (Exception ex)
        {
            Log.Error("Error rendering email template with Scriban: {Error}", ex.Message);
            return template; // Return original template on error
        }
    }

    /// <summary>
    /// Add CTA variables to Scriban context
    /// </summary>
    private void AddCTAToContext(ScriptObject context, NotificationSettings settings, string? templateButtonText = null, string? templateUrlOverride = null)
    {
        // Enable CTA if either:
        // 1. Global EnableCTA is true, OR
        // 2. Template has its own URL override (template-level opt-in)
        var ctaUrl = templateUrlOverride ?? settings.CTAUrl ?? "";
        var enableCta = settings.EnableCTA || !string.IsNullOrWhiteSpace(templateUrlOverride);
        
        context["EnableCTA"] = enableCta && !string.IsNullOrWhiteSpace(ctaUrl);
        context["CTAButtonText"] = templateButtonText ?? "Open Dashboard";
        context["CTAUrl"] = ctaUrl;
    }

    /// <summary>
    /// Load template from database or fall back to hardcoded template
    /// Returns (subject, body, ctaButtonText, ctaUrlOverride)
    /// </summary>
    private async Task<(string subject, string body, string? ctaButtonText, string? ctaUrlOverride)> LoadTemplateAsync(
        string templateType,
        string fallbackSubject,
        string fallbackBody)
    {
        try
        {
            var template = await _templateService.GetByTypeAsync(templateType);
            if (template != null)
            {
                return (template.Subject, template.HtmlBody, template.CTAButtonText, template.CTAUrlOverride);
            }
        }
        catch (Exception ex)
        {
            Log.Warning("Error loading template {TemplateType} from database, using fallback: {Error}", templateType, ex.Message);
        }

        return (fallbackSubject, fallbackBody, null, null);
    }

    // Context builders for each notification type

    private ScriptObject BuildSuccessEmailContext(ProfileExecution execution, Profile profile, NotificationSettings settings, string? ctaButtonText = null, string? ctaUrlOverride = null)
    {
        var context = new ScriptObject();
        context["ProfileName"] = profile.Name;
        context["ProfileId"] = profile.Id;
        context["ExecutionId"] = execution.Id;
        context["StartedAt"] = execution.StartedAt;
        context["CompletedAt"] = execution.CompletedAt;
        context["ExecutionTime"] = execution.ExecutionTimeMs.HasValue
            ? $"{execution.ExecutionTimeMs.Value / 1000.0:F2}s"
            : "N/A";
        context["RowCount"] = execution.RowCount?.ToString() ?? "0";
        context["FileSize"] = execution.FileSizeBytes.HasValue
            ? FormatFileSize(execution.FileSizeBytes.Value)
            : "N/A";
        context["OutputPath"] = execution.OutputPath ?? "N/A";
        AddCTAToContext(context, settings, ctaButtonText, ctaUrlOverride);
        return context;
    }

    private ScriptObject BuildFailureEmailContext(ProfileExecution execution, Profile profile, NotificationSettings settings, string? ctaButtonText = null, string? ctaUrlOverride = null)
    {
        var context = new ScriptObject();
        context["ProfileName"] = profile.Name;
        context["ProfileId"] = profile.Id;
        context["ExecutionId"] = execution.Id;
        context["StartedAt"] = execution.StartedAt;
        context["CompletedAt"] = execution.CompletedAt;
        context["ErrorMessage"] = execution.ErrorMessage ?? "Unknown error";
        AddCTAToContext(context, settings, ctaButtonText, ctaUrlOverride);
        return context;
    }

    private ScriptObject BuildImportSuccessEmailContext(ImportProfileExecution execution, ImportProfile profile, NotificationSettings settings, string? ctaButtonText = null, string? ctaUrlOverride = null)
    {
        var context = new ScriptObject();
        context["ProfileName"] = profile.Name;
        context["ProfileId"] = profile.Id;
        context["ExecutionId"] = execution.Id;
        context["StartedAt"] = execution.StartedAt;
        context["CompletedAt"] = execution.CompletedAt;
        context["ExecutionTime"] = execution.CompletedAt.HasValue
            ? $"{(execution.CompletedAt.Value - execution.StartedAt).TotalSeconds:F2}s"
            : "N/A";
        context["RowsImported"] = (execution.RowsInserted + execution.RowsUpdated).ToString();
        context["RowsFailed"] = execution.RowsFailed.ToString();
        AddCTAToContext(context, settings, ctaButtonText, ctaUrlOverride);
        return context;
    }

    private ScriptObject BuildImportFailureEmailContext(ImportProfileExecution execution, ImportProfile profile, NotificationSettings settings, string? ctaButtonText = null, string? ctaUrlOverride = null)
    {
        var context = new ScriptObject();
        context["ProfileName"] = profile.Name;
        context["ProfileId"] = profile.Id;
        context["ExecutionId"] = execution.Id;
        context["StartedAt"] = execution.StartedAt;
        context["CompletedAt"] = execution.CompletedAt;
        context["ErrorMessage"] = execution.ErrorMessage ?? "Unknown error";
        AddCTAToContext(context, settings, ctaButtonText, ctaUrlOverride);
        return context;
    }

    private ScriptObject BuildJobSuccessContext(int jobId, string jobName, Dictionary<string, object> jobDetails, NotificationSettings settings, string? ctaButtonText = null, string? ctaUrlOverride = null)
    {
        var context = new ScriptObject();
        context["JobId"] = jobId;
        context["JobName"] = jobName;
        foreach (var kvp in jobDetails)
        {
            context[kvp.Key] = kvp.Value;
        }
        AddCTAToContext(context, settings, ctaButtonText, ctaUrlOverride);
        return context;
    }

    private ScriptObject BuildJobFailureContext(int jobId, string jobName, string errorMessage, NotificationSettings settings, string? ctaButtonText = null, string? ctaUrlOverride = null)
    {
        var context = new ScriptObject();
        context["JobId"] = jobId;
        context["JobName"] = jobName;
        context["ErrorMessage"] = errorMessage;
        AddCTAToContext(context, settings, ctaButtonText, ctaUrlOverride);
        return context;
    }

    private ScriptObject BuildNewUserContext(string username, string email, NotificationSettings settings, string? ctaButtonText = null, string? ctaUrlOverride = null)
    {
        var context = new ScriptObject();
        context["Username"] = username;
        context["Email"] = email;
        AddCTAToContext(context, settings, ctaButtonText, ctaUrlOverride);
        return context;
    }

    private ScriptObject BuildNewApiKeyContext(string keyName, NotificationSettings settings, string? ctaButtonText = null, string? ctaUrlOverride = null)
    {
        var context = new ScriptObject();
        context["KeyName"] = keyName;
        AddCTAToContext(context, settings, ctaButtonText, ctaUrlOverride);
        return context;
    }

    private ScriptObject BuildNewWebhookContext(string webhookName, NotificationSettings settings, string? ctaButtonText = null, string? ctaUrlOverride = null)
    {
        var context = new ScriptObject();
        context["WebhookName"] = webhookName;
        AddCTAToContext(context, settings, ctaButtonText, ctaUrlOverride);
        return context;
    }

    private ScriptObject BuildEmailApprovalContext(int pendingCount, NotificationSettings settings, string? ctaButtonText = null, string? ctaUrlOverride = null)
    {
        var context = new ScriptObject();
        context["PendingCount"] = pendingCount;
        context["Plural"] = pendingCount != 1 ? "s" : "";
        context["PluralVerb"] = pendingCount != 1 ? "are" : "is";
        context["PluralThem"] = pendingCount != 1 ? "them" : "it";
        context["NotificationTime"] = DateTime.UtcNow;
        AddCTAToContext(context, settings, ctaButtonText, ctaUrlOverride);
        return context;
    }

    private ScriptObject BuildDatabaseSizeContext(long thresholdMb, long currentMb, long excessMb, NotificationSettings settings, string? ctaButtonText = null, string? ctaUrlOverride = null)
    {
        var context = new ScriptObject();
        context["ThresholdMB"] = thresholdMb;
        context["CurrentMB"] = currentMb;
        context["ExcessMB"] = excessMb;
        AddCTAToContext(context, settings, ctaButtonText, ctaUrlOverride);
        return context;
    }
}

/// <summary>
/// Notification configuration model (kept for backward compatibility with profile-level notifications)
/// </summary>
public class NotificationConfig
{
    public string? OnSuccess { get; set; } // email, webhook, both, none
    public string? OnFailure { get; set; } // email, webhook, both, none
    public string[]? Emails { get; set; }
    public string? WebhookUrl { get; set; }
}
