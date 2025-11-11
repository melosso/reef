// Source/Reef/Core/Services/NotificationService.cs
// Service for sending notifications about profile executions

using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;
using Reef.Core.Models;
using Serilog;
using System.Text.Json;

namespace Reef.Core.Services;

/// <summary>
/// Service for sending execution notifications via email and webhooks
/// </summary>
public class NotificationService
{
    private readonly IConfiguration _configuration;
    private readonly HttpClient _httpClient;

    public NotificationService(IConfiguration configuration)
    {
        _configuration = configuration;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    /// <summary>
    /// Send notification for successful execution
    /// </summary>
    public async Task SendExecutionSuccessAsync(ProfileExecution execution, Profile profile)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(profile.NotificationConfig))
            {
                return;
            }

            var config = JsonSerializer.Deserialize<NotificationConfig>(profile.NotificationConfig);
            if (config == null)
            {
                return;
            }

            // Send email notification if configured
            if (config.OnSuccess == "email" || config.OnSuccess == "both")
            {
                await SendSuccessEmailAsync(execution, profile, config);
            }

            // Send webhook notification if configured
            if (config.OnSuccess == "webhook" || config.OnSuccess == "both")
            {
                await SendSuccessWebhookAsync(execution, profile, config);
            }

            Log.Information("Sent success notification for execution {ExecutionId}", execution.Id);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error sending success notification for execution {ExecutionId}", execution.Id);
        }
    }

    /// <summary>
    /// Send notification for failed execution
    /// </summary>
    public async Task SendExecutionFailureAsync(ProfileExecution execution, Profile profile)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(profile.NotificationConfig))
            {
                return;
            }

            var config = JsonSerializer.Deserialize<NotificationConfig>(profile.NotificationConfig);
            if (config == null)
            {
                return;
            }

            // Send email notification if configured
            if (config.OnFailure == "email" || config.OnFailure == "both")
            {
                await SendFailureEmailAsync(execution, profile, config);
            }

            // Send webhook notification if configured
            if (config.OnFailure == "webhook" || config.OnFailure == "both")
            {
                await SendFailureWebhookAsync(execution, profile, config);
            }

            Log.Information("Sent failure notification for execution {ExecutionId}", execution.Id);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error sending failure notification for execution {ExecutionId}", execution.Id);
        }
    }

    /// <summary>
    /// Send email notification
    /// </summary>
    public async Task SendEmailAsync(string to, string subject, string body, bool isHtml = true)
    {
        try
        {
            var smtpHost = _configuration["Reef:Mail:SmtpHost"];
            var smtpPort = _configuration.GetValue<int>("Reef:Mail:SmtpPort", 587);
            var smtpUsername = _configuration["Reef:Mail:Username"];
            var smtpPassword = _configuration["Reef:Mail:Password"];
            var smtpFrom = _configuration["Reef:Mail:FromAddress"] ?? smtpUsername;

            if (string.IsNullOrWhiteSpace(smtpHost))
            {
                Log.Warning("SMTP host not configured, skipping email notification");
                return;
            }

            var message = new MimeMessage();
            message.From.Add(new MailboxAddress("Reef Notifications", smtpFrom));
            message.To.Add(MailboxAddress.Parse(to));
            message.Subject = subject;

            var bodyBuilder = new BodyBuilder();
            if (isHtml)
            {
                bodyBuilder.HtmlBody = body;
            }
            else
            {
                bodyBuilder.TextBody = body;
            }
            message.Body = bodyBuilder.ToMessageBody();

            using var client = new SmtpClient();
            await client.ConnectAsync(smtpHost, smtpPort, SecureSocketOptions.StartTls);
            
            if (!string.IsNullOrWhiteSpace(smtpUsername) && !string.IsNullOrWhiteSpace(smtpPassword))
            {
                await client.AuthenticateAsync(smtpUsername, smtpPassword);
            }

            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            Log.Information("Sent email to {To} with subject: {Subject}", to, subject);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error sending email to {To}", to);
            throw;
        }
    }

    /// <summary>
    /// Send webhook notification
    /// </summary>
    public async Task SendWebhookAsync(string url, object payload)
    {
        try
        {
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(url, content);

            if (response.IsSuccessStatusCode)
            {
                Log.Information("Sent webhook to {Url}", url);
            }
            else
            {
                Log.Warning("Webhook to {Url} returned status {StatusCode}", url, response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error sending webhook to {Url}", url);
            throw;
        }
    }

    /// <summary>
    /// Send success email notification
    /// </summary>
    private async Task SendSuccessEmailAsync(ProfileExecution execution, Profile profile, NotificationConfig config)
    {
        if (config.Emails == null || config.Emails.Length == 0)
        {
            return;
        }

        var subject = $"[Reef] Profile '{profile.Name}' executed successfully";
        var body = BuildSuccessEmailBody(execution, profile);

        foreach (var email in config.Emails)
        {
            try
            {
                await SendEmailAsync(email, subject, body, true);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to send success email to {Email}", email);
            }
        }
    }

    /// <summary>
    /// Send failure email notification
    /// </summary>
    private async Task SendFailureEmailAsync(ProfileExecution execution, Profile profile, NotificationConfig config)
    {
        if (config.Emails == null || config.Emails.Length == 0)
        {
            return;
        }

        var subject = $"[Reef] Profile '{profile.Name}' execution failed";
        var body = BuildFailureEmailBody(execution, profile);

        foreach (var email in config.Emails)
        {
            try
            {
                await SendEmailAsync(email, subject, body, true);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to send failure email to {Email}", email);
            }
        }
    }

    /// <summary>
    /// Send success webhook notification
    /// </summary>
    private async Task SendSuccessWebhookAsync(ProfileExecution execution, Profile profile, NotificationConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.WebhookUrl))
        {
            return;
        }

        var payload = new
        {
            eventType = "execution.success",
            timestamp = DateTime.UtcNow,
            profile = new
            {
                id = profile.Id,
                name = profile.Name
            },
            execution = new
            {
                id = execution.Id,
                startedAt = execution.StartedAt,
                completedAt = execution.CompletedAt,
                rowCount = execution.RowCount,
                outputPath = execution.OutputPath,
                fileSizeBytes = execution.FileSizeBytes,
                executionTimeMs = execution.ExecutionTimeMs
            }
        };

        await SendWebhookAsync(config.WebhookUrl, payload);
    }

    /// <summary>
    /// Send failure webhook notification
    /// </summary>
    private async Task SendFailureWebhookAsync(ProfileExecution execution, Profile profile, NotificationConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.WebhookUrl))
        {
            return;
        }

        var payload = new
        {
            eventType = "execution.failure",
            timestamp = DateTime.UtcNow,
            profile = new
            {
                id = profile.Id,
                name = profile.Name
            },
            execution = new
            {
                id = execution.Id,
                startedAt = execution.StartedAt,
                completedAt = execution.CompletedAt,
                errorMessage = execution.ErrorMessage
            }
        };

        await SendWebhookAsync(config.WebhookUrl, payload);
    }

    /// <summary>
    /// Build success email HTML body
    /// </summary>
    private string BuildSuccessEmailBody(ProfileExecution execution, Profile profile)
    {
        var executionTime = execution.ExecutionTimeMs.HasValue 
            ? $"{execution.ExecutionTimeMs / 1000.0:F2} seconds" 
            : "Unknown";

        var fileSize = execution.FileSizeBytes.HasValue
            ? FormatFileSize(execution.FileSizeBytes.Value)
            : "Unknown";

        return $@"
<!DOCTYPE html>
<html>
<head>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background-color: #10b981; color: white; padding: 20px; border-radius: 5px 5px 0 0; }}
        .content {{ background-color: #f9f9f9; padding: 20px; border-radius: 0 0 5px 5px; }}
        .detail {{ margin: 10px 0; }}
        .label {{ font-weight: bold; color: #666; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h2>✓ Profile Execution Successful</h2>
        </div>
        <div class='content'>
            <div class='detail'>
                <span class='label'>Profile:</span> {profile.Name}
            </div>
            <div class='detail'>
                <span class='label'>Execution ID:</span> {execution.Id}
            </div>
            <div class='detail'>
                <span class='label'>Started At:</span> {execution.StartedAt:yyyy-MM-dd HH:mm:ss} UTC
            </div>
            <div class='detail'>
                <span class='label'>Completed At:</span> {execution.CompletedAt:yyyy-MM-dd HH:mm:ss} UTC
            </div>
            <div class='detail'>
                <span class='label'>Execution Time:</span> {executionTime}
            </div>
            <div class='detail'>
                <span class='label'>Rows Exported:</span> {execution.RowCount:N0}
            </div>
            <div class='detail'>
                <span class='label'>File Size:</span> {fileSize}
            </div>
            <div class='detail'>
                <span class='label'>Output Path:</span> {execution.OutputPath}
            </div>
        </div>
    </div>
</body>
</html>";
    }

    /// <summary>
    /// Build failure email HTML body
    /// </summary>
    private string BuildFailureEmailBody(ProfileExecution execution, Profile profile)
    {
        return $@"
<!DOCTYPE html>
<html>
<head>
    <style>
        body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
        .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
        .header {{ background-color: #ef4444; color: white; padding: 20px; border-radius: 5px 5px 0 0; }}
        .content {{ background-color: #f9f9f9; padding: 20px; border-radius: 0 0 5px 5px; }}
        .detail {{ margin: 10px 0; }}
        .label {{ font-weight: bold; color: #666; }}
        .error {{ background-color: #fee2e2; border-left: 4px solid #ef4444; padding: 10px; margin: 15px 0; }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='header'>
            <h2>✗ Profile Execution Failed</h2>
        </div>
        <div class='content'>
            <div class='detail'>
                <span class='label'>Profile:</span> {profile.Name}
            </div>
            <div class='detail'>
                <span class='label'>Execution ID:</span> {execution.Id}
            </div>
            <div class='detail'>
                <span class='label'>Started At:</span> {execution.StartedAt:yyyy-MM-dd HH:mm:ss} UTC
            </div>
            <div class='detail'>
                <span class='label'>Failed At:</span> {execution.CompletedAt:yyyy-MM-dd HH:mm:ss} UTC
            </div>
            <div class='error'>
                <strong>Error Message:</strong><br>
                {execution.ErrorMessage}
            </div>
            <p>Please check the execution logs for more details.</p>
        </div>
    </div>
</body>
</html>";
    }

    /// <summary>
    /// Format file size in human-readable format
    /// </summary>
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
}

/// <summary>
/// Notification configuration model
/// </summary>
public class NotificationConfig
{
    public string? OnSuccess { get; set; } // email, webhook, both, none
    public string? OnFailure { get; set; } // email, webhook, both, none
    public string[]? Emails { get; set; }
    public string? WebhookUrl { get; set; }
}