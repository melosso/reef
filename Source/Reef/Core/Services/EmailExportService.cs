using System.Text.Json;
using MimeKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Reef.Core.Models;
using Reef.Core.TemplateEngines;
using Reef.Core.Destinations;
using Serilog;

namespace Reef.Core.Services;

/// <summary>
/// Service for exporting query results as templated emails
/// Converts query result rows to Scriban template context and sends via SMTP
/// </summary>
public class EmailExportService
{
    private readonly ScribanTemplateEngine _templateEngine;
    private static readonly Serilog.ILogger Log = Serilog.Log.ForContext<EmailExportService>();

    public EmailExportService(ScribanTemplateEngine templateEngine)
    {
        _templateEngine = templateEngine;
    }

    /// <summary>
    /// Partially mask email address for logging (e.g., "user@example.com" -> "us****@example.com")
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
    /// Export query results as templated emails
    /// Extracts recipients from query results, renders subject/body via Scriban, and sends via SMTP
    /// </summary>
    /// <param name="profile">Profile configuration (must have IsEmailExport=true)</param>
    /// <param name="emailDestination">Email destination for SMTP configuration</param>
    /// <param name="emailTemplate">Scriban template for email body (HTML)</param>
    /// <param name="queryResults">Query result rows from execution</param>
    /// <returns>Success status, message, success count, failure count, and split details</returns>
    public async Task<(bool Success, string? Message, int SuccessCount, int FailureCount, List<ProfileExecutionSplit> Splits)> ExportToEmailAsync(
        Profile profile,
        Destination emailDestination,
        QueryTemplate emailTemplate,
        List<Dictionary<string, object>> queryResults)
    {
        try
        {
            if (!profile.IsEmailExport)
            {
                return (false, "Profile is not configured as email export", 0, 0, new List<ProfileExecutionSplit>());
            }

            if (queryResults == null || queryResults.Count == 0)
            {
                Log.Information("No query results to email from profile {ProfileId}", profile.Id);
                return (true, "No rows returned from query", 0, 0, new List<ProfileExecutionSplit>());
            }

            if (string.IsNullOrEmpty(emailDestination.ConfigurationJson))
            {
                return (false, "Email destination configuration is empty", 0, 0, new List<ProfileExecutionSplit>());
            }

            var emailConfig = JsonSerializer.Deserialize<EmailDestinationConfiguration>(
                emailDestination.ConfigurationJson);

            if (emailConfig == null)
            {
                return (false, "Invalid email destination configuration", 0, 0, new List<ProfileExecutionSplit>());
            }

            // Validate template is present
            if (emailTemplate == null || string.IsNullOrEmpty(emailTemplate.Template))
            {
                return (false, "Email template not found or empty", 0, 0, new List<ProfileExecutionSplit>());
            }

            // Validate recipients column is configured
            if (string.IsNullOrEmpty(profile.EmailRecipientsColumn))
            {
                return (false, "Email recipients column not configured", 0, 0, new List<ProfileExecutionSplit>());
            }

            var successCount = 0;
            var failureCount = 0;
            var errors = new List<string>();
            var splits = new List<ProfileExecutionSplit>();

            // For email exports: split is enabled by default if a split key column is configured
            // If SplitKeyColumn is set: send one email per group (prevents duplicate recipients)
            // If SplitKeyColumn is empty: send one email per row
            if (!string.IsNullOrEmpty(profile.SplitKeyColumn))
            {
                // Group results by split key
                var groupedResults = queryResults
                    .GroupBy(r => SafeGetValue(r, profile.SplitKeyColumn)?.ToString() ?? "unknown")
                    .ToList();

                Log.Information("Sending {Count} emails (grouped by split key '{SplitKey}' to prevent duplicate recipients) from profile {ProfileId}",
                    groupedResults.Count, profile.SplitKeyColumn, profile.Id);

                foreach (var group in groupedResults)
                {
                    var result = await SendEmailBatchAsync(
                        profile,
                        emailConfig,
                        emailTemplate.Template,
                        group.ToList());

                    var splitKey = group.Key;
                    if (result.Success)
                    {
                        successCount++;
                        splits.Add(new ProfileExecutionSplit
                        {
                            SplitKey = splitKey,
                            Status = "Success",
                            RowCount = group.Count(),
                            CompletedAt = DateTime.UtcNow
                        });
                    }
                    else
                    {
                        failureCount++;
                        if (!string.IsNullOrEmpty(result.Error))
                        {
                            errors.Add(result.Error);
                        }
                        splits.Add(new ProfileExecutionSplit
                        {
                            SplitKey = splitKey,
                            Status = "Failed",
                            RowCount = group.Count(),
                            ErrorMessage = result.Error,
                            CompletedAt = DateTime.UtcNow
                        });
                    }
                }
            }
            else
            {
                // Send one email per row (or combined if recipients are same)
                Log.Debug("Sending {Count} emails from profile {ProfileId}",
                    queryResults.Count, profile.Id);

                for (int i = 0; i < queryResults.Count; i++)
                {
                    var row = queryResults[i];
                    var result = await SendEmailBatchAsync(
                        profile,
                        emailConfig,
                        emailTemplate.Template,
                        new List<Dictionary<string, object>> { row });

                    // Use row index or email address (masked) as split key when no split key column is set
                    var recipient = SafeGetValue(row, profile.EmailRecipientsColumn ?? string.Empty)?.ToString() ?? "unknown";
                    var splitKey = MaskEmailForLog(recipient);

                    if (result.Success)
                    {
                        successCount++;
                        splits.Add(new ProfileExecutionSplit
                        {
                            SplitKey = splitKey,
                            Status = "Success",
                            RowCount = 1,
                            CompletedAt = DateTime.UtcNow
                        });
                    }
                    else
                    {
                        failureCount++;
                        if (!string.IsNullOrEmpty(result.Error))
                        {
                            errors.Add(result.Error);
                        }
                        splits.Add(new ProfileExecutionSplit
                        {
                            SplitKey = splitKey,
                            Status = "Failed",
                            RowCount = 1,
                            ErrorMessage = result.Error,
                            CompletedAt = DateTime.UtcNow
                        });
                    }
                }
            }

            var message = $"Sent {successCount} email(s) successfully, {failureCount} failed";
            if (errors.Count > 0)
            {
                message += $". Errors: {string.Join("; ", errors.Take(3))}";
            }

            Log.Information(message);
            return (failureCount == 0, message, successCount, failureCount, splits);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to export query results as email from profile {ProfileId}: {Error}",
                profile.Id, ex.Message);
            return (false, $"Email export failed: {ex.Message}", 0, 0, new List<ProfileExecutionSplit>());
        }
    }

    /// <summary>
    /// Send email for a batch of rows (typically one row or one split group)
    /// </summary>
    private async Task<(bool Success, string? Error)> SendEmailBatchAsync(
        Profile profile,
        EmailDestinationConfiguration emailConfig,
        string emailBodyTemplate,
        List<Dictionary<string, object>> rows)
    {
        try
        {
            // Extract recipient from first row
            var firstRow = rows.First();
            var toRecipient = SafeGetValue(firstRow, profile.EmailRecipientsColumn ?? string.Empty)?.ToString();

            if (string.IsNullOrEmpty(toRecipient))
            {
                return (false, $"No recipient found in column '{profile.EmailRecipientsColumn}'");
            }

            // Extract CC recipient if configured
            var ccRecipient = string.IsNullOrEmpty(profile.EmailCcColumn)
                ? null
                : SafeGetValue(firstRow, profile.EmailCcColumn)?.ToString();

            // Extract subject from column if configured, otherwise use default
            var subject = $"Reef Export from {profile.Name}";
            if (!string.IsNullOrEmpty(profile.EmailSubjectColumn))
            {
                var subjectValue = SafeGetValue(firstRow, profile.EmailSubjectColumn)?.ToString();
                if (!string.IsNullOrEmpty(subjectValue))
                {
                    subject = subjectValue.Trim(); // Remove leading/trailing whitespace
                }
            }

            // Render email body from Scriban template
            string emailBody;
            try
            {
                emailBody = await _templateEngine.TransformAsync(rows, emailBodyTemplate);
            }
            catch (Exception ex)
            {
                return (false, $"Failed to render email body template: {ex.Message}");
            }

            // Check if multiple HTML documents were rendered (split by <!doctype html>)
            var htmlDocuments = SplitHtmlDocuments(emailBody);

            if (htmlDocuments.Count > 1 && rows.Count > 1)
            {
                // Multiple rows rendered multiple HTML documents - send one email per row
                Log.Information("Template rendered {HtmlCount} documents for {RowCount} rows. Sending separate emails.",
                    htmlDocuments.Count, rows.Count);

                var sendCount = 0;
                using var client = new SmtpClient();

                var smtpHost = emailConfig.SmtpServer ?? emailConfig.SmtpHost ?? "localhost";
                var port = emailConfig.SmtpPort > 0 ? emailConfig.SmtpPort : 587;
                var secureSocketOptions = GetSecureSocketOptions(emailConfig.SecurityMode);
                await client.ConnectAsync(smtpHost, port, secureSocketOptions);

                // Authenticate once for all messages
                await AuthenticateSmtpAsync(client, emailConfig);

                try
                {
                    // Send one email per HTML document
                    for (int i = 0; i < htmlDocuments.Count && i < rows.Count; i++)
                    {
                        var row = rows[i];
                        var html = htmlDocuments[i];

                        var recipient = SafeGetValue(row, profile.EmailRecipientsColumn ?? string.Empty)?.ToString();
                        if (string.IsNullOrEmpty(recipient))
                        {
                            Log.Warning("Row {RowNumber} has no recipient in column '{ColumnName}'", i, profile.EmailRecipientsColumn);
                            continue;
                        }

                        var cc = string.IsNullOrEmpty(profile.EmailCcColumn)
                            ? null
                            : SafeGetValue(row, profile.EmailCcColumn)?.ToString();

                        var rowSubject = subject;
                        if (!string.IsNullOrEmpty(profile.EmailSubjectColumn))
                        {
                            var subjectValue = SafeGetValue(row, profile.EmailSubjectColumn)?.ToString();
                            if (!string.IsNullOrEmpty(subjectValue))
                            {
                                rowSubject = subjectValue.Trim();
                            }
                        }

                        var message = new MimeMessage();
                        message.From.Add(new MailboxAddress(emailConfig.FromName ?? "Reef", emailConfig.FromAddress));

                        try
                        {
                            message.To.Add(ParseEmailWithAlias(recipient));
                        }
                        catch (ArgumentException ex)
                        {
                            Log.Error("Invalid recipient email format for row {RowNumber}: {Error}", i, ex.Message);
                            continue;
                        }

                        if (!string.IsNullOrEmpty(cc))
                        {
                            try
                            {
                                message.Cc.Add(ParseEmailWithAlias(cc));
                            }
                            catch (ArgumentException ex)
                            {
                                Log.Error("Invalid CC email format for row {RowNumber}: {Error}", i, ex.Message);
                                continue;
                            }
                        }

                        message.Subject = rowSubject;

                        var builder = new BodyBuilder { HtmlBody = html };
                        message.Body = builder.ToMessageBody();

                        await client.SendAsync(message);
                        sendCount++;

                        Log.Information("Email {Number} sent to {Recipient} from profile {ProfileId}",
                            i + 1, MaskEmailForLog(recipient), profile.Id);
                    }
                }
                finally
                {
                    await client.DisconnectAsync(true);
                }

                Log.Information("{Count} email(s) sent successfully from split HTML rendering", sendCount);
                return (sendCount > 0, sendCount == htmlDocuments.Count ? null : $"Sent {sendCount} of {htmlDocuments.Count} emails");
            }
            else
            {
                // Single HTML document - send to first row's recipient
                // Create MIME message
                var message = new MimeMessage();
                message.From.Add(new MailboxAddress(emailConfig.FromName ?? "Reef", emailConfig.FromAddress));

                try
                {
                    message.To.Add(ParseEmailWithAlias(toRecipient));
                }
                catch (ArgumentException ex)
                {
                    return (false, $"Invalid recipient email format: {ex.Message}");
                }

                if (!string.IsNullOrEmpty(ccRecipient))
                {
                    try
                    {
                        message.Cc.Add(ParseEmailWithAlias(ccRecipient));
                    }
                    catch (ArgumentException ex)
                    {
                        return (false, $"Invalid CC email format: {ex.Message}");
                    }
                }

                message.Subject = subject;

                // Build HTML body
                var builder = new BodyBuilder { HtmlBody = emailBody };
                message.Body = builder.ToMessageBody();

                // Send via SMTP
                using var client = new SmtpClient();

                var smtpHost = emailConfig.SmtpServer ?? emailConfig.SmtpHost ?? "localhost";
                var port = emailConfig.SmtpPort > 0 ? emailConfig.SmtpPort : 587;
                var secureSocketOptions = GetSecureSocketOptions(emailConfig.SecurityMode);
                await client.ConnectAsync(smtpHost, port, secureSocketOptions);

                // Authenticate based on auth type (only if server advertises auth capability)
                if (client.Capabilities.HasFlag(SmtpCapabilities.Authentication))
                {
                    var authType = emailConfig.SmtpAuthType ?? "Basic";
                    if (authType == "OAuth2")
                    {
                        // OAuth2 authentication
                        if (!string.IsNullOrEmpty(emailConfig.OauthToken) && !string.IsNullOrEmpty(emailConfig.OauthUsername))
                        {
                            try
                            {
                                var oauth2 = new SaslMechanismOAuth2(emailConfig.OauthUsername, emailConfig.OauthToken);
                                await client.AuthenticateAsync(oauth2);
                            }
                            catch (Exception ex)
                            {
                                Log.Error(ex, "OAuth2 authentication failed for {Server}", smtpHost);
                                throw;
                            }
                        }
                    }
                    else if (authType == "Basic")
                    {
                        // Basic authentication
                        var username = emailConfig.SmtpUsername ?? emailConfig.Username;
                        var password = emailConfig.SmtpPassword ?? emailConfig.Password;
                        if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
                        {
                            try
                            {
                                await client.AuthenticateAsync(username, password);
                            }
                            catch (Exception ex)
                            {
                                Log.Error(ex, "Basic authentication failed for {Server}", smtpHost);
                                throw;
                            }
                        }
                    }
                }
                else
                {
                    Log.Debug("SMTP server {Server} does not require authentication", smtpHost);
                }

                await client.SendAsync(message);
                await client.DisconnectAsync(true);

                Log.Information("Email sent successfully to {Recipient} from profile {ProfileId}",
                    MaskEmailForLog(toRecipient), profile.Id);

                return (true, null);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to send email batch from profile {ProfileId}: {Error}",
                profile.Id, ex.Message);
            return (false, ex.Message);
        }
    }

    /// <summary>
    /// Split rendered HTML output into individual HTML documents based on <!doctype html> delimiters
    /// </summary>
    private static List<string> SplitHtmlDocuments(string htmlOutput)
    {
        if (string.IsNullOrWhiteSpace(htmlOutput))
            return new List<string> { htmlOutput };

        var documents = htmlOutput
            .Split(new[] { "<!doctype html>" }, StringSplitOptions.None)
            .Where(doc => !string.IsNullOrWhiteSpace(doc))
            .Select(doc => "<!doctype html>" + doc)
            .ToList();

        return documents.Count > 0 ? documents : new List<string> { htmlOutput };
    }

    /// <summary>
    /// Authenticate SMTP client based on email destination configuration
    /// </summary>
    private static async Task AuthenticateSmtpAsync(SmtpClient client, EmailDestinationConfiguration emailConfig)
    {
        // Only authenticate if server advertises auth capability
        if (!client.Capabilities.HasFlag(SmtpCapabilities.Authentication))
        {
            Log.Debug("SMTP server does not advertise authentication capability");
            return;
        }

        var authType = emailConfig.SmtpAuthType ?? "Basic";
        if (authType == "OAuth2")
        {
            // OAuth2 authentication
            if (!string.IsNullOrEmpty(emailConfig.OauthToken) && !string.IsNullOrEmpty(emailConfig.OauthUsername))
            {
                try
                {
                    var oauth2 = new SaslMechanismOAuth2(emailConfig.OauthUsername, emailConfig.OauthToken);
                    await client.AuthenticateAsync(oauth2);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "OAuth2 authentication failed");
                    throw;
                }
            }
        }
        else if (authType == "Basic")
        {
            // Basic authentication
            var username = emailConfig.SmtpUsername ?? emailConfig.Username;
            var password = emailConfig.SmtpPassword ?? emailConfig.Password;
            if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
            {
                await client.AuthenticateAsync(username, password);
            }
        }
        // else: No authentication
    }

    /// <summary>
    /// Safely get a value from a row dictionary, handling case-insensitive column names
    /// </summary>
    private static object? SafeGetValue(Dictionary<string, object> row, string columnName)
    {
        if (string.IsNullOrEmpty(columnName))
            return null;

        // Try exact match first
        if (row.TryGetValue(columnName, out var value))
            return value;

        // Try case-insensitive match
        var key = row.Keys.FirstOrDefault(k =>
            k.Equals(columnName, StringComparison.OrdinalIgnoreCase));

        return key != null ? row[key] : null;
    }
}
