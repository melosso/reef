using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MimeKit;
using Serilog;

namespace Reef.Core.Destinations;

/// <summary>
/// SendGrid email provider - sends emails via SendGrid API
/// </summary>
public class SendGridEmailProvider : IEmailProvider
{
    private static readonly Serilog.ILogger Log = Serilog.Log.ForContext<SendGridEmailProvider>();
    private const string SendGridApiUrl = "https://api.sendgrid.com/v3/mail/send";

    /// <summary>
    /// Partially mask email address for logging
    /// </summary>
    private static string MaskEmailForLog(string email)
    {
        if (string.IsNullOrEmpty(email) || !email.Contains('@'))
            return "***@***";

        var parts = email.Split('@');
        if (parts.Length != 2)
            return "***@***";

        var localPart = parts[0];
        var domain = parts[1];

        if (localPart.Length <= 2)
            return $"**@{domain}";

        var maskedLocal = localPart[0].ToString() + new string('*', localPart.Length - 2) + localPart[^1];
        return $"{maskedLocal}@{domain}";
    }

    /// <summary>
    /// Extract email address from "Name;email@address.com" format
    /// </summary>
    private static string ExtractEmailAddress(string emailString)
    {
        if (string.IsNullOrWhiteSpace(emailString))
            return emailString;

        var parts = emailString.Split(';');
        return parts.Length > 1 ? parts[^1].Trim() : emailString.Trim();
    }

    /// <summary>
    /// Extract display name from "Name;email@address.com" format
    /// </summary>
    private static string? ExtractDisplayName(string emailString)
    {
        if (string.IsNullOrWhiteSpace(emailString))
            return null;

        var parts = emailString.Split(';');
        if (parts.Length <= 1)
            return null;

        var name = parts[0].Trim();
        return string.IsNullOrEmpty(name) ? null : name;
    }

    /// <summary>
    /// Send email message via SendGrid API
    /// </summary>
    public async Task<(bool Success, string? FinalPath, string? ErrorMessage)> SendEmailAsync(
        MimeMessage message,
        EmailDestinationConfiguration config)
    {
        try
        {
            // Validate SendGrid configuration
            if (string.IsNullOrEmpty(config.SendGridApiKey))
            {
                return (false, null, "SendGrid API key is required");
            }

            var maskedRecipients = string.Join(", ", message.To.OfType<MailboxAddress>().Select(r => MaskEmailForLog(r.Address)));
            Log.Information("Sending email via SendGrid to {Recipients}", maskedRecipients);

            // Read file attachment
            byte[]? attachmentData = null;
            string? attachmentFileName = null;

            if (message.Attachments.Any())
            {
                var attachment = message.Attachments.First();
                if (attachment is MimePart mimePart)
                {
                    using (var memoryStream = new MemoryStream())
                    {
                        mimePart.Content.DecodeTo(memoryStream);
                        attachmentData = memoryStream.ToArray();
                        attachmentFileName = mimePart.FileName ?? "attachment";
                    }
                }
            }

            // Prepare SendGrid request - use recipients from MimeMessage (they're already parsed)
            var sendGridRequest = new SendGridEmailRequest
            {
                From = new SendGridEmailAddress
                {
                    Email = config.FromAddress,
                    Name = config.FromName
                },
                Subject = message.Subject,
                Content = new[]
                {
                    new SendGridContent
                    {
                        Type = "text/html",
                        Value = message.HtmlBody ?? message.TextBody ?? string.Empty
                    }
                },
                Personalizations = new[]
                {
                    new SendGridPersonalization
                    {
                        To = message.To.OfType<MailboxAddress>()
                            .Select(r => new SendGridEmailAddress
                            {
                                Email = r.Address,
                                Name = r.Name
                            }).ToArray(),
                        Cc = message.Cc.OfType<MailboxAddress>()
                            .Select(c => new SendGridEmailAddress
                            {
                                Email = c.Address,
                                Name = c.Name
                            }).ToArray()
                    }
                }
            };

            // Add attachment if present
            if (attachmentData != null && attachmentFileName != null)
            {
                sendGridRequest.Attachments = new[]
                {
                    new SendGridAttachment
                    {
                        Filename = attachmentFileName,
                        Content = Convert.ToBase64String(attachmentData),
                        Type = "application/octet-stream"
                    }
                };
            }

            // Make API request
            using (var httpClient = new HttpClient())
            {
                // Set timeout
                if (config.TimeoutSeconds > 0)
                {
                    httpClient.Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds);
                }

                // Add authorization header with Bearer token
                httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", config.SendGridApiKey);

                var jsonContent = new StringContent(
                    JsonSerializer.Serialize(sendGridRequest, new JsonSerializerOptions
                    {
                        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                    }),
                    Encoding.UTF8,
                    "application/json");

                var response = await httpClient.PostAsync(SendGridApiUrl, jsonContent);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Log.Error("SendGrid API error ({StatusCode}): {Error}", response.StatusCode, errorContent);
                    return (false, null, $"SendGrid API error: {response.StatusCode} - {errorContent}");
                }

                // SendGrid returns 202 Accepted with no body on success
                var recipients = string.Join(", ", message.To.OfType<MailboxAddress>().Select(r => r.Address));
                var maskedRecipientsForLog = string.Join(", ", message.To.OfType<MailboxAddress>().Select(r => MaskEmailForLog(r.Address)));
                var messageId = response.Headers.Contains("X-Message-Id")
                    ? response.Headers.GetValues("X-Message-Id").FirstOrDefault()
                    : "unknown";

                Log.Information("Email sent successfully via SendGrid to {Recipients}", maskedRecipientsForLog);

                return (true, $"Email sent to {recipients} (ID: {messageId})", null);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to send email via SendGrid: {Error}", ex.Message);
            return (false, null, $"SendGrid send failed: {ex.Message}");
        }
    }

    // SendGrid API models
    private class SendGridEmailRequest
    {
        [JsonPropertyName("from")]
        public SendGridEmailAddress? From { get; set; }

        [JsonPropertyName("subject")]
        public string? Subject { get; set; }

        [JsonPropertyName("content")]
        public SendGridContent[]? Content { get; set; }

        [JsonPropertyName("personalizations")]
        public SendGridPersonalization[]? Personalizations { get; set; }

        [JsonPropertyName("attachments")]
        public SendGridAttachment[]? Attachments { get; set; }
    }

    private class SendGridEmailAddress
    {
        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    private class SendGridContent
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("value")]
        public string? Value { get; set; }
    }

    private class SendGridPersonalization
    {
        [JsonPropertyName("to")]
        public SendGridEmailAddress[]? To { get; set; }

        [JsonPropertyName("cc")]
        public SendGridEmailAddress[]? Cc { get; set; }
    }

    private class SendGridAttachment
    {
        [JsonPropertyName("filename")]
        public string? Filename { get; set; }

        [JsonPropertyName("content")]
        public string? Content { get; set; }

        [JsonPropertyName("type")]
        public string? Type { get; set; }
    }
}
