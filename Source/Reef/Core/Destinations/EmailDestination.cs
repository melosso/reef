using System.Text.Json;
using MimeKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Serilog;

namespace Reef.Core.Destinations;

/// <summary>
/// Email destination - sends exported files as email attachments
/// </summary>
public class EmailDestination : IDestination
{
    private static readonly Serilog.ILogger Log = Serilog.Log.ForContext<EmailDestination>();

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
    /// Parse email address with optional display name using ";" separator
    /// Supports formats: "email@example.com" or "Display Name;email@example.com"
    /// </summary>
    private static MailboxAddress ParseEmailWithAlias(string emailString)
    {
        if (string.IsNullOrWhiteSpace(emailString))
            throw new ArgumentException("Email address cannot be empty");

        var parts = emailString.Split(';');

        if (parts.Length == 1)
        {
            // Just email address, no display name
            var email = emailString.Trim();

            // Basic email validation: must contain @ and have text on both sides
            if (!email.Contains("@") || email.StartsWith("@") || email.EndsWith("@"))
            {
                throw new ArgumentException($"Invalid email address: {emailString}");
            }

            try
            {
                return MailboxAddress.Parse(email);
            }
            catch
            {
                throw new ArgumentException($"Invalid email address: {emailString}");
            }
        }
        else if (parts.Length == 2)
        {
            // Format: "Display Name;email@address"
            var displayName = parts[0].Trim();
            var email = parts[1].Trim();

            if (string.IsNullOrWhiteSpace(email))
                throw new ArgumentException($"Email address cannot be empty: {emailString}");

            // Basic email validation: must contain @ and have text on both sides
            if (!email.Contains("@") || email.StartsWith("@") || email.EndsWith("@"))
            {
                throw new ArgumentException($"Invalid email address: {email}");
            }

            // Sanitize display name - remove control characters and CRLF sequences that could cause header injection
            displayName = System.Text.RegularExpressions.Regex.Replace(displayName, @"[\r\n\0]", "");

            try
            {
                // Validate email format
                var addr = MailboxAddress.Parse(email);
                return new MailboxAddress(displayName, addr.Address);
            }
            catch
            {
                throw new ArgumentException($"Invalid email address: {email}");
            }
        }
        else
        {
            // More than one semicolon - take first part as name, rest as email
            var displayName = parts[0].Trim();
            var email = string.Join(";", parts.Skip(1)).Trim();

            // Basic email validation: must contain @ and have text on both sides
            if (!email.Contains("@") || email.StartsWith("@") || email.EndsWith("@"))
            {
                throw new ArgumentException($"Invalid email address: {email}");
            }

            // Sanitize display name
            displayName = System.Text.RegularExpressions.Regex.Replace(displayName, @"[\r\n\0]", "");

            try
            {
                var addr = MailboxAddress.Parse(email);
                return new MailboxAddress(displayName, addr.Address);
            }
            catch
            {
                throw new ArgumentException($"Invalid email address: {email}");
            }
        }
    }

    /// <summary>
    /// Save file to destination by sending it as an email attachment
    /// </summary>
    public async Task<(bool Success, string? FinalPath, string? ErrorMessage)> SaveAsync(
        string sourcePath,
        string destinationConfig)
    {
        try
        {
            if (!File.Exists(sourcePath))
            {
                return (false, null, "Source file not found");
            }

            var config = JsonSerializer.Deserialize<EmailDestinationConfiguration>(destinationConfig);
            if (config == null)
            {
                return (false, null, "Invalid email configuration");
            }

            // Validate basic configuration
            if (string.IsNullOrEmpty(config.FromAddress))
            {
                return (false, null, "From address is required");
            }

            if (config.ToAddresses == null || config.ToAddresses.Count == 0)
            {
                return (false, null, "At least one recipient address is required");
            }

            // Validate provider-specific requirements
            var provider = config.EmailProvider?.ToLower() ?? "smtp";
            if (provider == "resend")
            {
                if (string.IsNullOrEmpty(config.ResendApiKey))
                {
                    return (false, null, "Resend API key is required");
                }
            }
            else if (provider == "sendgrid")
            {
                if (string.IsNullOrEmpty(config.SendGridApiKey))
                {
                    return (false, null, "SendGrid API key is required");
                }
            }
            else if (provider == "smtp")
            {
                if (string.IsNullOrEmpty(config.SmtpHost))
                {
                    return (false, null, "SMTP host is required");
                }
            }
            else
            {
                return (false, null, $"Unknown email provider: {provider}");
            }

            // Create email message
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(config.FromName ?? "Reef Export Service", config.FromAddress));

            // Add recipients
            foreach (var recipient in config.ToAddresses)
            {
                if (!string.IsNullOrEmpty(recipient))
                {
                    try
                    {
                        message.To.Add(ParseEmailWithAlias(recipient));
                    }
                    catch (ArgumentException ex)
                    {
                        return (false, null, $"Invalid recipient email format: {ex.Message}");
                    }
                }
            }

            // Add CC recipients if provided
            if (config.CcAddresses != null)
            {
                foreach (var cc in config.CcAddresses)
                {
                    if (!string.IsNullOrEmpty(cc))
                    {
                        try
                        {
                            message.Cc.Add(ParseEmailWithAlias(cc));
                        }
                        catch (ArgumentException ex)
                        {
                            return (false, null, $"Invalid CC email format: {ex.Message}");
                        }
                    }
                }
            }

            var fileName = Path.GetFileName(sourcePath);
            message.Subject = config.Subject ?? $"Reef Export: {fileName}";

            // Build message body
            var builder = new BodyBuilder();

            // Set body text
            if (!string.IsNullOrEmpty(config.Body))
            {
                if (config.IsHtml)
                {
                    builder.HtmlBody = config.Body;
                }
                else
                {
                    builder.TextBody = config.Body;
                }
            }
            else
            {
                // Default body
                builder.TextBody = $@"Export File Attached

File: {fileName}
Size: {new FileInfo(sourcePath).Length:N0} bytes
Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC

This is an automated message from Reef.";
            }

            // Attach file
            Log.Debug("Attaching file {FileName} to email", fileName);
            builder.Attachments.Add(sourcePath);
            message.Body = builder.ToMessageBody();

            // Route to appropriate provider
            IEmailProvider emailProvider = provider switch
            {
                "resend" => new ResendEmailProvider(),
                "sendgrid" => new SendGridEmailProvider(),
                _ => new SmtpEmailProvider() // Default to SMTP
            };

            return await emailProvider.SendEmailAsync(message, config);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to send email: {Error}", ex.Message);
            return (false, null, $"Email send failed: {ex.Message}");
        }
    }
}

/// <summary>
/// Email destination configuration
/// </summary>
public class EmailDestinationConfiguration
{
    // Email provider selection
    public string? EmailProvider { get; set; } = "SMTP"; // SMTP or Resend

    // Server configuration (SMTP only)
    public string SmtpServer { get; set; } = string.Empty;
    public string SmtpHost { get; set; } = string.Empty; // Alias for backward compatibility
    public int SmtpPort { get; set; } = 587;
    public bool EnableSsl { get; set; } = true;
    public bool UseSsl { get; set; } = true; // Alias for backward compatibility

    // Authentication (SMTP)
    public string? SmtpAuthType { get; set; } = "Basic"; // Basic, OAuth2, None
    public string? SmtpUsername { get; set; }
    public string? Username { get; set; } // Alias for backward compatibility
    public string? SmtpPassword { get; set; }
    public string? Password { get; set; } // Alias for backward compatibility
    public string? OauthToken { get; set; }
    public string? OauthUsername { get; set; }

    // Resend configuration
    public string? ResendApiKey { get; set; }

    // SendGrid configuration
    public string? SendGridApiKey { get; set; }

    // Email configuration
    public string FromAddress { get; set; } = string.Empty;
    public string? FromName { get; set; } = "Reef Export Service";
    public List<string> ToAddresses { get; set; } = new();
    public List<string>? CcAddresses { get; set; }
    public string? Subject { get; set; }
    public string? Body { get; set; }
    public bool IsHtml { get; set; } = false;

    // Connection settings
    public int TimeoutSeconds { get; set; } = 30;
    public int RetryCount { get; set; } = 3;
    public bool ValidateCertificate { get; set; } = true;
    public string? SecurityMode { get; set; } = "StartTls"; // StartTls, None, Auto
}
