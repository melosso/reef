// Source/Reef/Core/Services/NotificationService.cs
// Service for sending system notifications via email destinations

using Dapper;
using Microsoft.Data.Sqlite;
using Reef.Core.Destinations;
using Reef.Core.Models;
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

    public NotificationService(
        string connectionString,
        EncryptionService encryptionService,
        NotificationThrottler throttler,
        NotificationTemplateService templateService)
    {
        _connectionString = connectionString;
        _encryptionService = encryptionService;
        _throttler = throttler;
        _templateService = templateService;
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
            Log.Error(ex, "Error retrieving notification settings from database");
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
            Log.Error(ex, "Error retrieving destination {DestinationId}", destinationId);
            return null;
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
                Log.Debug("Profile success notification throttled for profile {ProfileId}", profile.Id);
                return;
            }

            var subject = $"[Reef] Profile '{profile.Name}' executed successfully";
            var placeholders = BuildSuccessEmailPlaceholders(execution, profile);
            var body = BuildSuccessEmailBodyTemplate();
            var replacedBody = ReplacePlaceholders(body, placeholders);

            await SendSystemNotificationAsync(subject, replacedBody, settings);
            Log.Information("Sent success notification for execution {ExecutionId} (profile {ProfileId})",
                execution.Id, profile.Id);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error sending success notification for execution {ExecutionId}", execution.Id);
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
                Log.Debug("Profile failure notification throttled for profile {ProfileId}", profile.Id);
                return;
            }

            var subject = $"[Reef] Profile '{profile.Name}' execution failed";
            var placeholders = BuildFailureEmailPlaceholders(execution, profile);
            var body = BuildFailureEmailBodyTemplate();
            var replacedBody = ReplacePlaceholders(body, placeholders);

            await SendSystemNotificationAsync(subject, replacedBody, settings);
            Log.Information("Sent failure notification for execution {ExecutionId} (profile {ProfileId})",
                execution.Id, profile.Id);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error sending failure notification for execution {ExecutionId}", execution.Id);
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

            var subject = $"[Reef] Job '{jobName}' completed successfully";
            var body = BuildJobSuccessEmailBody(jobName, jobDetails);

            await SendSystemNotificationAsync(subject, body, settings);
            Log.Information("Sent job success notification for job {JobId} ({JobName})", jobId, jobName);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error sending job success notification for {JobName}", jobName);
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

            var subject = $"[Reef] Job '{jobName}' failed";
            var body = BuildJobFailureEmailBody(jobName, errorMessage);

            await SendSystemNotificationAsync(subject, body, settings);
            Log.Information("Sent job failure notification for job {JobId} ({JobName})", jobId, jobName);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error sending job failure notification for {JobName}", jobName);
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

            var subject = $"[Reef] New user created: {username}";
            var body = BuildNewUserEmailBody(username, email);

            await SendSystemNotificationAsync(subject, body, settings);
            Log.Information("Sent new user notification for {Username}", username);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error sending new user notification for {Username}", username);
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

            var subject = $"[Reef] New API key created: {keyName}";
            var body = BuildNewApiKeyEmailBody(keyName);

            await SendSystemNotificationAsync(subject, body, settings);
            Log.Information("Sent new API key notification for {KeyName}", keyName);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error sending new API key notification for {KeyName}", keyName);
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

            var subject = $"[Reef] New webhook created: {webhookName}";
            var body = BuildNewWebhookEmailBody(webhookName);

            await SendSystemNotificationAsync(subject, body, settings);
            Log.Information("Sent new webhook notification for {WebhookName}", webhookName);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error sending new webhook notification for {WebhookName}", webhookName);
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
            var subject = $"[Reef] Database size critical: {currentMb}MB (threshold: {thresholdMb}MB)";

            var body = BuildDatabaseSizeEmailBody(thresholdMb, currentMb, excessMb);

            await SendSystemNotificationAsync(subject, body, settings);
            Log.Information("Sent database size threshold notification ({CurrentMb}MB > {ThresholdMb}MB)",
                currentMb, thresholdMb);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error sending database size notification");
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
                        Log.Warning(ex, "Invalid recipient email address: {Email}", recipient);
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

            // Route to appropriate provider based on configuration
            IEmailProvider emailProvider = (emailConfig.EmailProvider?.ToLower()) switch
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
            Log.Error(ex, "Error sending system notification");
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
    /// Get template content with placeholder substitution
    /// Loads from database if available, falls back to hardcoded defaults
    /// </summary>
    private async Task<(string? Subject, string? Body)> GetTemplateAsync(
        string templateType,
        Dictionary<string, string> placeholders)
    {
        try
        {
            // Try to load from database first
            var template = await _templateService.GetByTypeAsync(templateType);

            if (template != null)
            {
                // Replace placeholders in subject and body
                var subject = ReplacePlaceholders(template.Subject, placeholders);
                var body = ReplacePlaceholders(template.HtmlBody, placeholders);
                return (subject, body);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error loading template {TemplateType} from database, falling back to defaults", templateType);
        }

        // Fallback to hardcoded defaults based on template type
        // This method will be called from the existing builder methods
        return (null, null);
    }

    /// <summary>
    /// Replace placeholders in template strings
    /// Supports timezone-aware formatting: {VariableName.GMT+1} or {VariableName.yyyy-MM-dd.GMT+1}
    /// Regular placeholders: {PlaceholderName}
    /// </summary>
    private string ReplacePlaceholders(string text, Dictionary<string, string> placeholders)
    {
        if (string.IsNullOrEmpty(text) || placeholders == null || placeholders.Count == 0)
            return text;

        // First replace regular placeholders
        foreach (var kvp in placeholders)
        {
            var placeholder = $"{{{kvp.Key}}}";
            text = text.Replace(placeholder, kvp.Value ?? "", StringComparison.Ordinal);
        }

        // Then replace timezone-aware placeholders
        text = ReplaceTimezoneAwarePlaceholders(text, placeholders);

        return text;
    }

    /// <summary>
    /// Replace timezone-aware placeholders like {CreatedAt.GMT+1} or {StartedAt.yyyy-MM-dd.GMT-5}
    /// Extracts the value from placeholders dictionary and formats with timezone offset
    /// </summary>
    private static string ReplaceTimezoneAwarePlaceholders(string text, Dictionary<string, string> placeholders)
    {
        if (string.IsNullOrEmpty(text) || placeholders == null || placeholders.Count == 0)
            return text;

        // Match pattern: {VariableName.OptionalFormat.GMT±Offset}
        var pattern = @"\{(\w+)(?:\.([^\}\.]*?))?\.GMT([+-])(\d+)\}";
        return System.Text.RegularExpressions.Regex.Replace(text, pattern, match =>
        {
            var variableName = match.Groups[1].Value;
            var formatString = match.Groups[2].Value;
            var offsetSign = match.Groups[3].Value;
            var offsetValue = int.Parse(match.Groups[4].Value);

            // Get the value from placeholders
            if (!placeholders.TryGetValue(variableName, out var value) || value == null)
                return match.Value; // Return unchanged if variable not found

            // Try to parse as DateTime
            if (!DateTime.TryParse(value, out var dateTime))
                return value; // Return as-is if not a valid DateTime

            // Apply timezone offset
            var offset = TimeSpan.FromHours(offsetSign == "+" ? offsetValue : -offsetValue);
            var offsetDateTime = dateTime.Add(offset);

            // Format the date
            if (string.IsNullOrEmpty(formatString))
                formatString = "yyyy-MM-dd HH:mm:ss"; // Default format

            return offsetDateTime.ToString(formatString);
        });
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
