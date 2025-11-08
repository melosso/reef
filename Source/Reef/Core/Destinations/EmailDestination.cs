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

            // Validate configuration
            if (string.IsNullOrEmpty(config.SmtpHost))
            {
                return (false, null, "SMTP host is required");
            }

            if (string.IsNullOrEmpty(config.FromAddress))
            {
                return (false, null, "From address is required");
            }

            if (config.ToAddresses == null || config.ToAddresses.Count == 0)
            {
                return (false, null, "At least one recipient address is required");
            }

            // Create email message
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(config.FromName ?? "Reef Export Service", config.FromAddress));
            
            // Add recipients
            foreach (var recipient in config.ToAddresses)
            {
                if (!string.IsNullOrEmpty(recipient))
                {
                    message.To.Add(MailboxAddress.Parse(recipient));
                }
            }

            // Add CC recipients if provided
            if (config.CcAddresses != null)
            {
                foreach (var cc in config.CcAddresses)
                {
                    if (!string.IsNullOrEmpty(cc))
                    {
                        message.Cc.Add(MailboxAddress.Parse(cc));
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
                builder.TextBody = $@"Reef Export File Attached

File: {fileName}
Size: {new FileInfo(sourcePath).Length:N0} bytes
Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC

This is an automated message from Reef Export Service.";
            }
            
            // Attach file
            Log.Debug("Attaching file {FileName} to email", fileName);
            builder.Attachments.Add(sourcePath);
            message.Body = builder.ToMessageBody();

            // Send email
            Log.Information("Sending email to {Recipients} via {SmtpHost}:{SmtpPort}", 
                string.Join(", ", config.ToAddresses), config.SmtpHost, config.SmtpPort);

            using var client = new SmtpClient();
            
            // Connect to SMTP server
            await client.ConnectAsync(config.SmtpHost, config.SmtpPort, 
                config.UseSsl ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto);
            
            // Authenticate if credentials provided
            if (!string.IsNullOrEmpty(config.Username))
            {
                await client.AuthenticateAsync(config.Username, config.Password);
            }
            
            // Send message
            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            var recipients = string.Join(", ", config.ToAddresses);
            Log.Information("Email sent successfully to {Recipients}", recipients);

            return (true, $"Email sent to {recipients}", null);
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
    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 587;
    public bool UseSsl { get; set; } = true;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string FromAddress { get; set; } = string.Empty;
    public string? FromName { get; set; } = "Reef Export Service";
    public List<string> ToAddresses { get; set; } = new();
    public List<string>? CcAddresses { get; set; }
    public string? Subject { get; set; }
    public string? Body { get; set; }
    public bool IsHtml { get; set; } = false;
}
