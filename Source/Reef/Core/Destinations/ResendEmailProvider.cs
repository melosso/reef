using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MimeKit;
using Reef.Core.Services;
using Serilog;

namespace Reef.Core.Destinations;

/// <summary>
/// Resend email provider - sends emails via Resend API
/// </summary>
public class ResendEmailProvider : IEmailProvider
{
    private static readonly Serilog.ILogger Log = Serilog.Log.ForContext<ResendEmailProvider>();
    private const string ResendApiUrl = "https://api.resend.com/emails";

    // Rate limiter: 2 requests per second (Resend API limit)
    private static readonly RateLimiter RateLimiter = new RateLimiter(requestsPerSecond: 2.0, burstSize: 1);

    // Retry configuration
    private const int MaxRetries = 3;
    private const int InitialRetryDelayMs = 100;

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
    /// Send email message via Resend API with rate limiting and retry logic
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

            var maskedRecipients = string.Join(", ", message.To.OfType<MailboxAddress>().Select(r => MaskEmailForLog(r.Address)));
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

            // Prepare Resend request - use recipients from MimeMessage (they're already parsed)
            // Resend API expects "to" and "cc" as string arrays with format "Name <email@address.com>" or just "email@address.com"
            var resendRequest = new ResendEmailRequest
            {
                From = $"{config.FromName} <{config.FromAddress}>",
                To = message.To.OfType<MailboxAddress>().Select(r =>
                    string.IsNullOrEmpty(r.Name) ? r.Address : $"{r.Name} <{r.Address}>"
                ).ToArray(),
                Cc = message.Cc.OfType<MailboxAddress>().Select(c =>
                    string.IsNullOrEmpty(c.Name) ? c.Address : $"{c.Name} <{c.Address}>"
                ).ToArray(),
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

            // Send with retry logic and rate limiting
            return await SendWithRetryAsync(resendRequest, config, message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to send email via Resend: {Error}", ex.Message);
            return (false, null, $"Resend send failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Send email with exponential backoff retry logic and rate limiting
    /// </summary>
    private async Task<(bool Success, string? FinalPath, string? ErrorMessage)> SendWithRetryAsync(
        ResendEmailRequest request,
        EmailDestinationConfiguration config,
        MimeMessage message,
        int attemptNumber = 0)
    {
        try
        {
            // Acquire rate limit token before making request
            await RateLimiter.AcquireAsync(tokensNeeded: 1);

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
                    JsonSerializer.Serialize(request, new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull }),
                    Encoding.UTF8,
                    "application/json");

                var response = await httpClient.PostAsync(ResendApiUrl, jsonContent);

                // Handle rate limiting (429 Too Many Requests)
                if ((int)response.StatusCode == 429)
                {
                    if (attemptNumber < MaxRetries)
                    {
                        // Calculate retry delay
                        int retryDelayMs;

                        // Check for Retry-After header
                        if (response.Headers.RetryAfter != null)
                        {
                            if (response.Headers.RetryAfter.Delta.HasValue)
                            {
                                retryDelayMs = (int)response.Headers.RetryAfter.Delta.Value.TotalMilliseconds;
                            }
                            else if (response.Headers.RetryAfter.Date.HasValue)
                            {
                                retryDelayMs = Math.Max(100, (int)(response.Headers.RetryAfter.Date.Value - DateTimeOffset.UtcNow).TotalMilliseconds);
                            }
                            else
                            {
                                // Exponential backoff if Retry-After is unparseable
                                retryDelayMs = InitialRetryDelayMs * (int)Math.Pow(2, attemptNumber);
                            }
                        }
                        else
                        {
                            // Exponential backoff with jitter
                            var baseDelay = InitialRetryDelayMs * (int)Math.Pow(2, attemptNumber);
                            var jitter = new Random().Next(0, baseDelay / 2);
                            retryDelayMs = baseDelay + jitter;
                        }

                        Log.Warning("Rate limit exceeded (429). Retrying in {RetryDelayMs}ms (attempt {AttemptNumber} of {MaxRetries})",
                            retryDelayMs, attemptNumber + 1, MaxRetries);

                        await Task.Delay(retryDelayMs);

                        // Recursive retry
                        return await SendWithRetryAsync(request, config, message, attemptNumber + 1);
                    }
                    else
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        Log.Error("Resend API rate limit exceeded after {MaxRetries} retries: {Error}", MaxRetries, errorContent);
                        return (false, null, $"Resend API rate limit exceeded (429) after {MaxRetries} retries - {errorContent}");
                    }
                }

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Log.Error("Resend API error ({StatusCode}): {Error}", response.StatusCode, errorContent);
                    return (false, null, $"Resend API error: {response.StatusCode} - {errorContent}");
                }

                var jsonString = await response.Content.ReadAsStringAsync();
                var responseData = JsonSerializer.Deserialize<ResendEmailResponse>(jsonString);
                var recipients = string.Join(", ", message.To.OfType<MailboxAddress>().Select(r => r.Address));
                var maskedRecipientsForLog = string.Join(", ", message.To.OfType<MailboxAddress>().Select(r => MaskEmailForLog(r.Address)));

                Log.Information("Email sent successfully via Resend (MessageId: {MessageId}) to {Recipients}",
                    responseData?.Id, maskedRecipientsForLog);

                return (true, $"Email sent to {recipients} (ID: {responseData?.Id})", null);
            }
        }
        catch (TaskCanceledException ex)
        {
            // Timeout occurred
            if (attemptNumber < MaxRetries)
            {
                var retryDelayMs = InitialRetryDelayMs * (int)Math.Pow(2, attemptNumber);
                Log.Warning(ex, "Request timeout. Retrying in {RetryDelayMs}ms (attempt {AttemptNumber} of {MaxRetries})",
                    retryDelayMs, attemptNumber + 1, MaxRetries);
                await Task.Delay(retryDelayMs);
                return await SendWithRetryAsync(request, config, message, attemptNumber + 1);
            }
            else
            {
                Log.Error(ex, "Request timeout after {MaxRetries} retries", MaxRetries);
                return (false, null, $"Request timeout after {MaxRetries} retries: {ex.Message}");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error sending email via Resend (attempt {AttemptNumber}): {Error}", attemptNumber + 1, ex.Message);
            return (false, null, $"Resend send failed: {ex.Message}");
        }
    }

    // Resend API models
    private class ResendEmailRequest
    {
        [JsonPropertyName("from")]
        public string? From { get; set; }

        [JsonPropertyName("to")]
        public string[]? To { get; set; }

        [JsonPropertyName("cc")]
        public string[]? Cc { get; set; }

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
