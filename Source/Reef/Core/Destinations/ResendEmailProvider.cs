using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MimeKit;
using Serilog;

namespace Reef.Core.Destinations;

/// <summary>
/// Resend email provider - sends emails via Resend API
/// </summary>
public class ResendEmailProvider : IEmailProvider
{
    private static readonly Serilog.ILogger Log = Serilog.Log.ForContext<ResendEmailProvider>();
    private const string ResendApiUrl = "https://api.resend.com/emails";

    /// <summary>
    /// Partially mask email address for logging
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
    /// Send email message via Resend API
    /// </summary>
    public async Task<(bool Success, string? FinalPath, string? ErrorMessage)> SendEmailAsync(
        MimeMessage message,
        EmailDestinationConfiguration config)
    {
        try
        {
            // Validate Resend configuration
            if (string.IsNullOrEmpty(config.ResendApiKey))
            {
                return (false, null, "Resend API key is required");
            }

            var maskedRecipients = string.Join(", ", config.ToAddresses.Select(MaskEmailForLog));
            Log.Information("Sending email via Resend to {Recipients}", maskedRecipients);

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

            // Prepare Resend request
            var resendRequest = new ResendEmailRequest
            {
                From = $"{config.FromName} <{config.FromAddress}>",
                To = config.ToAddresses.Select(r => new ResendRecipient
                {
                    Email = ExtractEmailAddress(r),
                    Name = ExtractDisplayName(r)
                }).ToArray(),
                Cc = config.CcAddresses?.Where(c => !string.IsNullOrEmpty(c))
                    .Select(c => new ResendRecipient
                    {
                        Email = ExtractEmailAddress(c),
                        Name = ExtractDisplayName(c)
                    }).ToArray(),
                Subject = message.Subject,
                Html = message.HtmlBody,
                Text = message.TextBody,
                ReplyTo = (message.ReplyTo.FirstOrDefault() as MailboxAddress)?.Address
            };

            // Add attachment if present
            if (attachmentData != null && attachmentFileName != null)
            {
                resendRequest.Attachments = new[]
                {
                    new ResendAttachment
                    {
                        Filename = attachmentFileName,
                        Content = Convert.ToBase64String(attachmentData)
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

                // Add authorization header
                httpClient.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", config.ResendApiKey);

                var jsonContent = new StringContent(
                    JsonSerializer.Serialize(resendRequest, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull }),
                    Encoding.UTF8,
                    "application/json");

                var response = await httpClient.PostAsync(ResendApiUrl, jsonContent);

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Log.Error("Resend API error ({StatusCode}): {Error}", response.StatusCode, errorContent);
                    return (false, null, $"Resend API error: {response.StatusCode} - {errorContent}");
                }

                var jsonString = await response.Content.ReadAsStringAsync();
                var responseData = JsonSerializer.Deserialize<ResendEmailResponse>(jsonString);
                var recipients = string.Join(", ", config.ToAddresses);
                var maskedRecipientsForLog = string.Join(", ", config.ToAddresses.Select(MaskEmailForLog));

                Log.Information("Email sent successfully via Resend (MessageId: {MessageId}) to {Recipients}",
                    responseData?.Id, maskedRecipientsForLog);

                return (true, $"Email sent to {recipients} (ID: {responseData?.Id})", null);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to send email via Resend: {Error}", ex.Message);
            return (false, null, $"Resend send failed: {ex.Message}");
        }
    }

    // Resend API models
    private class ResendEmailRequest
    {
        [JsonPropertyName("from")]
        public string? From { get; set; }

        [JsonPropertyName("to")]
        public ResendRecipient[]? To { get; set; }

        [JsonPropertyName("cc")]
        public ResendRecipient[]? Cc { get; set; }

        [JsonPropertyName("subject")]
        public string? Subject { get; set; }

        [JsonPropertyName("html")]
        public string? Html { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }

        [JsonPropertyName("reply_to")]
        public string? ReplyTo { get; set; }

        [JsonPropertyName("attachments")]
        public ResendAttachment[]? Attachments { get; set; }
    }

    private class ResendRecipient
    {
        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    private class ResendAttachment
    {
        [JsonPropertyName("filename")]
        public string? Filename { get; set; }

        [JsonPropertyName("content")]
        public string? Content { get; set; }
    }

    private class ResendEmailResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }
    }
}
