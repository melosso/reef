using MimeKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Serilog;

namespace Reef.Core.Destinations;

/// <summary>
/// SMTP email provider - sends emails via SMTP server
/// </summary>
public class SmtpEmailProvider : IEmailProvider
{
    private static readonly Serilog.ILogger Log = Serilog.Log.ForContext<SmtpEmailProvider>();

    /// <summary>
    /// Convert security mode string to SecureSocketOptions
    /// </summary>
    private static SecureSocketOptions GetSecureSocketOptions(string? securityMode)
    {
        return (securityMode?.ToLower()) switch
        {
            "none" => SecureSocketOptions.None,
            "auto" => SecureSocketOptions.Auto,
            "starttls" => SecureSocketOptions.StartTls,
            _ => SecureSocketOptions.StartTls // Default to StartTls
        };
    }

    /// <summary>
    /// Send email message via SMTP
    /// </summary>
    public async Task<(bool Success, string? FinalPath, string? ErrorMessage)> SendEmailAsync(
        MimeMessage message,
        EmailDestinationConfiguration config)
    {
        try
        {
            // Get SMTP host (check both SmtpHost and SmtpServer for backward compatibility)
            var smtpHost = !string.IsNullOrEmpty(config.SmtpHost) ? config.SmtpHost : config.SmtpServer;

            // Validate SMTP configuration
            if (string.IsNullOrEmpty(smtpHost))
            {
                return (false, null, "SMTP host is required");
            }

            var maskedRecipients = string.Join(", ", config.ToAddresses.Select(MaskEmailForLog));
            Log.Information("Sending email via SMTP to {Recipients} using {SmtpHost}:{SmtpPort}",
                maskedRecipients, smtpHost, config.SmtpPort);

            using var client = new SmtpClient();

            // Set timeout
            if (config.TimeoutSeconds > 0)
            {
                client.Timeout = config.TimeoutSeconds * 1000;
            }

            // Connect to SMTP server with security mode from configuration
            var secureSocketOptions = GetSecureSocketOptions(config.SecurityMode);
            await client.ConnectAsync(smtpHost, config.SmtpPort, secureSocketOptions);

            // Get username and password (check both SmtpUsername/Username and SmtpPassword/Password for backward compatibility)
            var username = !string.IsNullOrEmpty(config.SmtpUsername) ? config.SmtpUsername : config.Username;
            var password = !string.IsNullOrEmpty(config.SmtpPassword) ? config.SmtpPassword : config.Password;

            // Authenticate if credentials provided
            if (!string.IsNullOrEmpty(username))
            {
                await client.AuthenticateAsync(username, password);
            }

            // Send message
            var response = await client.SendAsync(message);
            await client.DisconnectAsync(true);

            var recipients = string.Join(", ", config.ToAddresses);
            var maskedRecipientsForLog = string.Join(", ", config.ToAddresses.Select(MaskEmailForLog));
            Log.Information("Email sent successfully via SMTP to {Recipients}", maskedRecipientsForLog);

            return (true, $"Email sent to {recipients}", null);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to send email via SMTP: {Error}", ex.Message);
            return (false, null, $"SMTP send failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Partially mask email address for logging (e.g., "user@example.com" -> "u*@example.com")
    /// </summary>
    private static string MaskEmailForLog(string email)
    {
        if (string.IsNullOrEmpty(email) || !email.Contains("@"))
            return "***@***";

        var parts = email.Split('@');
        if (parts.Length != 2)
            return "***@***";

        var localPart = parts[0];
        var domain = parts[1];

        // Show first 2 chars + last char of local part, mask the rest
        if (localPart.Length <= 2)
            return $"**@{domain}";

        var maskedLocal = localPart[0].ToString() + new string('*', localPart.Length - 2) + localPart[^1];
        return $"{maskedLocal}@{domain}";
    }
}
