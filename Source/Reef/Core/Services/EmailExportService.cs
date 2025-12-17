using System.Text.Json;
using MimeKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Reef.Core.Models;
using Reef.Core.TemplateEngines;
using Reef.Core.DocumentGeneration;
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
    private readonly QueryTemplateService _queryTemplateService;
    private readonly DocumentTemplateEngine _documentTemplateEngine;
    private readonly AttachmentVariableResolver _variableResolver;
    private readonly BinaryAttachmentResolver _binaryResolver;
    private readonly HashSet<string> _deduplicationCache;
    private static readonly Serilog.ILogger Log = Serilog.Log.ForContext<EmailExportService>();

    public EmailExportService(
        ScribanTemplateEngine templateEngine,
        QueryTemplateService queryTemplateService,
        DocumentTemplateEngine documentTemplateEngine)
    {
        _templateEngine = templateEngine;
        _queryTemplateService = queryTemplateService;
        _documentTemplateEngine = documentTemplateEngine;
        _variableResolver = new AttachmentVariableResolver();
        _binaryResolver = new BinaryAttachmentResolver(_variableResolver);
        _deduplicationCache = new HashSet<string>();
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

            // Validate recipients are configured (either column or hardcoded)
            if (string.IsNullOrEmpty(profile.EmailRecipientsColumn) && string.IsNullOrEmpty(profile.EmailRecipientsHardcoded))
            {
                return (false, "Email recipients column or hardcoded email not configured", 0, 0, new List<ProfileExecutionSplit>());
            }

            var successCount = 0;
            var failureCount = 0;
            var errors = new List<string>();
            var splits = new List<ProfileExecutionSplit>();

            // Parse attachment configuration if present
            AttachmentConfig? attachmentConfig = null;
            if (!string.IsNullOrEmpty(profile.EmailAttachmentConfig))
            {
                try
                {
                    attachmentConfig = JsonSerializer.Deserialize<AttachmentConfig>(profile.EmailAttachmentConfig);
                    Log.Debug("Parsed attachment configuration for profile {ProfileId}: enabled={Enabled}, mode={Mode}",
                        profile.Id, attachmentConfig?.Enabled, attachmentConfig?.Mode);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to parse attachment configuration for profile {ProfileId}", profile.Id);
                }
            }

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
                        group.ToList(),
                        attachmentConfig);

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
                // Check if using hardcoded recipient or if all rows have same recipient
                // If yes, send ONE email with ALL rows
                // If no, send one email per row (different recipients)
                
                bool useHardcodedRecipient = profile.UseHardcodedRecipients;
                bool allSameRecipient = true;
                string? firstRecipient = null;
                
                if (!useHardcodedRecipient && queryResults.Count > 1)
                {
                    firstRecipient = SafeGetValue(queryResults[0], profile.EmailRecipientsColumn ?? string.Empty)?.ToString();
                    for (int i = 1; i < queryResults.Count; i++)
                    {
                        var currentRecipient = SafeGetValue(queryResults[i], profile.EmailRecipientsColumn ?? string.Empty)?.ToString();
                        if (currentRecipient != firstRecipient)
                        {
                            allSameRecipient = false;
                            break;
                        }
                    }
                }
                
                if (useHardcodedRecipient || allSameRecipient)
                {
                    // Send ALL rows in ONE email
                    Log.Information("Sending 1 email with {RowCount} rows from profile {ProfileId}",
                        queryResults.Count, profile.Id);
                    
                    var result = await SendEmailBatchAsync(
                        profile,
                        emailConfig,
                        emailTemplate.Template,
                        queryResults,
                        attachmentConfig);
                    
                    var recipient = useHardcodedRecipient 
                        ? (profile.EmailRecipientsHardcoded ?? "unknown")
                        : (firstRecipient ?? "unknown");
                    var splitKey = MaskEmailForLog(recipient);
                    
                    if (result.Success)
                    {
                        successCount++;
                        splits.Add(new ProfileExecutionSplit
                        {
                            SplitKey = splitKey,
                            Status = "Success",
                            RowCount = queryResults.Count,
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
                            RowCount = queryResults.Count,
                            ErrorMessage = result.Error,
                            CompletedAt = DateTime.UtcNow
                        });
                    }
                }
                else
                {
                    // Send one email per row (different recipients)
                    Log.Information("Sending {Count} emails (one per row, different recipients) from profile {ProfileId}",
                        queryResults.Count, profile.Id);

                    for (int i = 0; i < queryResults.Count; i++)
                    {
                        var row = queryResults[i];
                        var result = await SendEmailBatchAsync(
                            profile,
                            emailConfig,
                            emailTemplate.Template,
                            new List<Dictionary<string, object>> { row },
                            attachmentConfig);

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
        List<Dictionary<string, object>> rows,
        AttachmentConfig? attachmentConfig = null)
    {
        try
        {
            // Resolve attachments if configured
            var attachments = ResolveAttachmentsForBatch(rows, attachmentConfig, profile);

            // Extract recipient from first row
            var firstRow = rows.First();
            var toRecipient = profile.UseHardcodedRecipients
                ? profile.EmailRecipientsHardcoded
                : SafeGetValue(firstRow, profile.EmailRecipientsColumn ?? string.Empty)?.ToString();

            if (string.IsNullOrEmpty(toRecipient))
            {
                return (false, $"No recipient found in column '{profile.EmailRecipientsColumn}'");
            }

            // Extract CC recipient if configured
            var ccRecipient = profile.UseHardcodedCc
                ? profile.EmailCcHardcoded
                : (string.IsNullOrEmpty(profile.EmailCcColumn)
                    ? null
                    : SafeGetValue(firstRow, profile.EmailCcColumn)?.ToString());

            // Extract subject from hardcoded value, column, or use default
            string? subject = null;
            if (profile.UseHardcodedSubject && !string.IsNullOrEmpty(profile.EmailSubjectHardcoded))
            {
                // Process hardcoded subject through Scriban template engine to support variables
                try
                {
                    // Create system context with profile metadata and current date/time
                    var now = DateTime.Now;

                    var systemContext = new Dictionary<string, object>
                    {
                        { "system", new Dictionary<string, object>
                        {
                            { "profileId", profile.Id },
                            { "profileNumber", profile.Id },
                            { "profileName", profile.Name },
                            { "date", now.ToString("yyyy-MM-dd") },
                            { "time", now.ToString("HH:mm:ss") },
                            { "datetime", now.ToString("yyyy-MM-dd HH:mm:ss") },
                            { "timestamp", now.ToString("O") }, // ISO 8601 format
                            { "now", now }
                        }}
                    };

                    subject = await _templateEngine.TransformAsync(new List<Dictionary<string, object>> { firstRow }, profile.EmailSubjectHardcoded, systemContext);
                    subject = subject?.Trim();
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to process hardcoded subject template: {Subject}", profile.EmailSubjectHardcoded);
                    subject = profile.EmailSubjectHardcoded?.Trim();
                }
            }
            else if (!string.IsNullOrEmpty(profile.EmailSubjectColumn))
            {
                subject = SafeGetValue(firstRow, profile.EmailSubjectColumn)?.ToString()?.Trim();
            }

            if (string.IsNullOrEmpty(subject))
            {
                subject = $"Reef Export from {profile.Name}";
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

            // Don't split HTML documents if using DocumentTemplate attachments
            // The attachment contains all the data, email is just a notification
            bool hasDocumentTemplateAttachment = attachmentConfig != null && 
                                                  attachmentConfig.Enabled && 
                                                  attachmentConfig.Mode == "DocumentTemplate";

            if (htmlDocuments.Count > 1 && rows.Count > 1 && !hasDocumentTemplateAttachment)
            {
                // Multiple rows rendered multiple HTML documents - send one email per row
                Log.Information("Template rendered {HtmlCount} documents for {RowCount} rows. Sending separate emails.",
                    htmlDocuments.Count, rows.Count);

                var sendCount = 0;

                // Check if using non-SMTP provider
                var provider = emailConfig.EmailProvider?.ToLower() ?? "smtp";
                if (provider != "smtp")
                {
                    // For Resend/SendGrid, send each HTML document as a separate email
                    IEmailProvider emailProvider = provider switch
                    {
                        "resend" => new ResendEmailProvider(),
                        "sendgrid" => new SendGridEmailProvider(),
                        _ => new SmtpEmailProvider()
                    };

                    for (int i = 0; i < htmlDocuments.Count && i < rows.Count; i++)
                    {
                        var row = rows[i];
                        var html = htmlDocuments[i];

                        var recipient = profile.UseHardcodedRecipients
                            ? profile.EmailRecipientsHardcoded
                            : SafeGetValue(row, profile.EmailRecipientsColumn ?? string.Empty)?.ToString();
                        if (string.IsNullOrEmpty(recipient))
                        {
                            Log.Warning("Row {RowNumber} has no recipient in column '{ColumnName}'", i, profile.EmailRecipientsColumn);
                            continue;
                        }

                        var cc = profile.UseHardcodedCc
                            ? profile.EmailCcHardcoded
                            : (string.IsNullOrEmpty(profile.EmailCcColumn)
                                ? null
                                : SafeGetValue(row, profile.EmailCcColumn)?.ToString());

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
                        AttachFilesToMessage(builder, attachments);
                        message.Body = builder.ToMessageBody();

                        var result = await emailProvider.SendEmailAsync(message, emailConfig);
                        if (result.Success)
                        {
                            sendCount++;
                            Log.Information("Email {Number} sent to {Recipient} from profile {ProfileId}",
                                i + 1, MaskEmailForLog(recipient), profile.Id);
                        }
                        else
                        {
                            Log.Warning("Failed to send email {Number} to {Recipient}: {Error}",
                                i + 1, MaskEmailForLog(recipient), result.ErrorMessage);
                        }
                    }

                    Log.Information("{Count} email(s) sent successfully from split HTML rendering", sendCount);
                    return (sendCount > 0, sendCount == htmlDocuments.Count ? null : $"Sent {sendCount} of {htmlDocuments.Count} emails");
                }

                // SMTP provider with multiple HTML documents
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

                        var recipient = profile.UseHardcodedRecipients
                            ? profile.EmailRecipientsHardcoded
                            : SafeGetValue(row, profile.EmailRecipientsColumn ?? string.Empty)?.ToString();
                        if (string.IsNullOrEmpty(recipient))
                        {
                            Log.Warning("Row {RowNumber} has no recipient in column '{ColumnName}'", i, profile.EmailRecipientsColumn);
                            continue;
                        }

                        var cc = profile.UseHardcodedCc
                            ? profile.EmailCcHardcoded
                            : (string.IsNullOrEmpty(profile.EmailCcColumn)
                                ? null
                                : SafeGetValue(row, profile.EmailCcColumn)?.ToString());

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
                        AttachFilesToMessage(builder, attachments);
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
                AttachFilesToMessage(builder, attachments);
                message.Body = builder.ToMessageBody();

                // Send via appropriate provider
                var provider = emailConfig.EmailProvider?.ToLower() ?? "smtp";
                if (provider != "smtp")
                {
                    // Use Resend/SendGrid provider
                    IEmailProvider emailProvider = provider switch
                    {
                        "resend" => new ResendEmailProvider(),
                        "sendgrid" => new SendGridEmailProvider(),
                        _ => new SmtpEmailProvider()
                    };

                    var result = await emailProvider.SendEmailAsync(message, emailConfig);
                    if (result.Success)
                    {
                        Log.Information("Email sent successfully to {Recipient} from profile {ProfileId}",
                            MaskEmailForLog(toRecipient), profile.Id);
                        return (true, null);
                    }
                    else
                    {
                        return (false, result.ErrorMessage);
                    }
                }

                // SMTP provider
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
    /// Resolves attachments for a batch of rows based on attachment configuration
    /// </summary>
    private List<EmailAttachment> ResolveAttachmentsForBatch(
        List<Dictionary<string, object>> rows,
        AttachmentConfig? attachmentConfig,
        Profile profile)
    {
        var attachments = new List<EmailAttachment>();

        if (attachmentConfig == null || !attachmentConfig.Enabled)
            return attachments;

        if (attachmentConfig.Mode == "DocumentTemplate")
        {
            return ResolveDocumentTemplateAttachments(rows, attachmentConfig, profile);
        }
        else if (attachmentConfig.Mode != "Binary" || attachmentConfig.Binary == null)
        {
            Log.Debug("Attachment mode '{Mode}' not supported yet", attachmentConfig.Mode);
            return attachments;
        }

        // Process each row
        foreach (var row in rows)
        {
            try
            {
                var rowAttachments = _binaryResolver.ResolveAttachmentsForRow(
                    row,
                    attachmentConfig.Binary,
                    profile.Name,
                    profile.Id);

                foreach (var attachment in rowAttachments)
                {
                    // Apply deduplication if configured
                    if (attachmentConfig.Deduplication == "ByFilename")
                    {
                        string cacheKey = attachment.Filename;
                        if (_deduplicationCache.Contains(cacheKey))
                        {
                            Log.Debug("Skipping duplicate attachment '{Filename}' (by filename)", attachment.Filename);
                            continue;
                        }
                        _deduplicationCache.Add(cacheKey);
                    }
                    else if (attachmentConfig.Deduplication == "ByHash")
                    {
                        string contentHash = ComputeHash(attachment.Content);
                        if (_deduplicationCache.Contains(contentHash))
                        {
                            Log.Debug("Skipping duplicate attachment '{Filename}' (by content hash)", attachment.Filename);
                            continue;
                        }
                        _deduplicationCache.Add(contentHash);
                    }

                    attachments.Add(attachment);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error resolving attachments for row in profile '{ProfileName}'", profile.Name);
                // Continue with other rows
            }
        }

        // Validate limits
        ValidateAttachmentLimits(attachments, attachmentConfig);

        return attachments;
    }

    /// <summary>
    /// Resolves DocumentTemplate attachments - generates PDF/DOCX from template for rows
    /// </summary>
    private List<EmailAttachment> ResolveDocumentTemplateAttachments(
        List<Dictionary<string, object>> rows,
        AttachmentConfig attachmentConfig,
        Profile profile)
    {
        var attachments = new List<EmailAttachment>();

        if (attachmentConfig.DocumentTemplate == null)
        {
            Log.Warning("DocumentTemplate attachment config is null for profile {ProfileId}", profile.Id);
            return attachments;
        }

        try
        {
            var templateId = attachmentConfig.DocumentTemplate.TemplateId;
            var filenameColumn = attachmentConfig.DocumentTemplate.FilenameColumnName;

            Log.Information("Generating DocumentTemplate attachment for profile {ProfileId} using template {TemplateId}",
                profile.Id, templateId);

            // 1. Load template from database
            var template = _queryTemplateService.GetByIdAsync(templateId).GetAwaiter().GetResult();
            if (template == null)
            {
                Log.Warning("Template {TemplateId} not found for DocumentTemplate attachment in profile {ProfileId}",
                    templateId, profile.Id);
                return attachments;
            }

            // 2. Determine filename
            string filename;
            if (!string.IsNullOrEmpty(filenameColumn) && rows.Count > 0)
            {
                var filenameValue = SafeGetValue(rows[0], filenameColumn)?.ToString();
                filename = !string.IsNullOrEmpty(filenameValue) 
                    ? filenameValue 
                    : $"{profile.Name}_{DateTime.Now:yyyyMMdd_HHmmss}";
            }
            else
            {
                filename = $"{profile.Name}_{DateTime.Now:yyyyMMdd_HHmmss}";
            }

            // 3. Build context for filename template processing
            var context = new Dictionary<string, object>
            {
                { "profile_name", profile.Name },
                { "profile_id", profile.Id },
                { "execution_id", 0 }, // TODO: Pass actual execution ID if available
                { "timestamp", DateTime.Now.ToString("yyyyMMdd_HHmmss") },
                { "date", DateTime.Now.ToString("yyyyMMdd") },
                { "time", DateTime.Now.ToString("HHmmss") },
                { "guid", Guid.NewGuid().ToString() }
            };

            // 4. Generate document using DocumentTemplateEngine
            var outputPath = _documentTemplateEngine.TransformAsync(
                rows,
                template.Template,
                context,
                filenameTemplate: null).GetAwaiter().GetResult();

            Log.Debug("Document generated at path: {OutputPath}", outputPath);

            // 5. Read file bytes from disk
            var documentBytes = File.ReadAllBytes(outputPath);

            // 6. Determine content type based on file extension
            var contentType = Path.GetExtension(outputPath).ToLowerInvariant() switch
            {
                ".pdf" => "application/pdf",
                ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                _ => "application/octet-stream"
            };

            // 7. Create attachment
            var attachment = new EmailAttachment
            {
                Filename = Path.GetFileName(outputPath),
                Content = documentBytes,
                ContentType = contentType
            };

            attachments.Add(attachment);

            Log.Information("Generated DocumentTemplate attachment '{Filename}' ({Size} bytes) for profile {ProfileId}",
                attachment.Filename, attachment.Content.Length, profile.Id);

            // 8. Optionally delete temp file (keep it for now for debugging)
            // File.Delete(outputPath);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to generate DocumentTemplate attachment for profile {ProfileId}", profile.Id);
        }

        return attachments;
    }

    /// <summary>
    /// Sanitizes filename to remove invalid characters
    /// </summary>
    private string SanitizeFilename(string filename)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = string.Concat(filename.Select(c => invalidChars.Contains(c) ? '_' : c));
        return sanitized;
    }

    /// <summary>
    /// Validates attachment limits (count and size)
    /// </summary>
    private void ValidateAttachmentLimits(List<EmailAttachment> attachments, AttachmentConfig config)
    {
        if (attachments.Count > config.MaxAttachmentsPerEmail)
        {
            Log.Warning("Attachment count ({Count}) exceeds limit ({Max}). Truncating.",
                attachments.Count, config.MaxAttachmentsPerEmail);
            // In production, might want to fail or skip, but for now we'll keep first N
        }

        const long MAX_EMAIL_SIZE = 25_000_000; // 25 MB
        long totalSize = attachments.Sum(a => (long)a.Content.Length);
        if (totalSize > MAX_EMAIL_SIZE)
        {
            Log.Warning("Total attachment size ({Size} bytes) exceeds email limit ({Max} bytes)",
                totalSize, MAX_EMAIL_SIZE);
        }
    }

    /// <summary>
    /// Computes MD5 hash of byte array for deduplication
    /// </summary>
    private string ComputeHash(byte[] data)
    {
        using (var md5 = System.Security.Cryptography.MD5.Create())
        {
            byte[] hash = md5.ComputeHash(data);
            return Convert.ToHexString(hash);
        }
    }

    /// <summary>
    /// Attaches resolved attachments to a BodyBuilder
    /// </summary>
    private void AttachFilesToMessage(BodyBuilder builder, List<EmailAttachment> attachments)
    {
        if (attachments == null || attachments.Count == 0)
            return;

        foreach (var attachment in attachments)
        {
            try
            {
                builder.Attachments.Add(attachment.Filename, attachment.Content, new MimeKit.ContentType(attachment.ContentType.Split('/')[0], attachment.ContentType.Split('/')[1]));
                Log.Debug("Attached file '{Filename}' ({Size} bytes) to email", attachment.Filename, attachment.Content.Length);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Error attaching file '{Filename}'", attachment.Filename);
            }
        }
    }

    /// <summary>
    /// Render email details for approval workflow without actually sending
    /// Returns recipients, subject, HTML body, and attachment config
    /// </summary>
    public async Task<(List<(string Recipients, string Subject, string HtmlBody, string? CcAddresses, string? AttachmentConfigJson, string? ReefId, string? DeltaSyncHash)> RenderedEmails, List<string> Errors)> RenderEmailsForApprovalAsync(
        Profile profile,
        QueryTemplate emailTemplate,
        List<Dictionary<string, object>> queryResults,
        AttachmentConfig? attachmentConfig = null,
        Dictionary<string, string>? deltaSyncHashes = null)
    {
        var renderedEmails = new List<(string, string, string, string?, string?, string?, string?)>();
        var errors = new List<string>();

        try
        {
            if (queryResults == null || queryResults.Count == 0)
            {
                Log.Information("No query results to render for email approval from profile {ProfileId}", profile.Id);
                return (renderedEmails, errors);
            }

            if (string.IsNullOrEmpty(emailTemplate?.Template))
            {
                errors.Add("Email template not found or empty");
                return (renderedEmails, errors);
            }

            // Render each row as an email
            foreach (var row in queryResults)
            {
                try
                {
                    // Extract recipients
                    var recipients = profile.UseHardcodedRecipients
                        ? profile.EmailRecipientsHardcoded
                        : (string.IsNullOrEmpty(profile.EmailRecipientsColumn) ? null : SafeGetValue(row, profile.EmailRecipientsColumn)?.ToString());

                    if (string.IsNullOrEmpty(recipients))
                    {
                        Log.Warning("No recipients found for row in profile {ProfileId}", profile.Id);
                        continue;
                    }

                    // Extract CC addresses
                    var ccAddresses = profile.UseHardcodedCc
                        ? profile.EmailCcHardcoded
                        : (string.IsNullOrEmpty(profile.EmailCcColumn) ? null : SafeGetValue(row, profile.EmailCcColumn)?.ToString());

                    // Extract subject
                    var subject = (profile.UseHardcodedSubject
                        ? profile.EmailSubjectHardcoded
                        : (string.IsNullOrEmpty(profile.EmailSubjectColumn) ? "[No Subject]" : SafeGetValue(row, profile.EmailSubjectColumn)?.ToString() ?? "[No Subject]")) ?? "[No Subject]";

                    // Render HTML body using Scriban template
                    var htmlBody = await _templateEngine.TransformAsync(
                        new List<Dictionary<string, object>> { row },
                        emailTemplate.Template);

                    // Serialize attachment config
                    var attachmentConfigJson = attachmentConfig != null
                        ? JsonSerializer.Serialize(attachmentConfig)
                        : null;

                    // Extract ReefId for delta sync tracking
                    string? reefId = null;
                    string? reefIdNormalized = null;
                    if (!string.IsNullOrEmpty(profile.DeltaSyncReefIdColumn))
                    {
                        var rawReefId = SafeGetValue(row, profile.DeltaSyncReefIdColumn);
                        if (rawReefId != null)
                        {
                            reefId = rawReefId.ToString();
                            // Normalize ReefId for hash lookup (must match ProcessDeltaAsync normalization)
                            reefIdNormalized = NormalizeReefId(rawReefId, profile.DeltaSyncReefIdNormalization ?? "Trim");
                        }
                    }

                    // Get delta sync hash for this reef_id if available
                    // Use normalized ReefId for lookup since NewHashState uses normalized keys
                    string? deltaSyncHash = null;
                    if (!string.IsNullOrEmpty(reefIdNormalized) && deltaSyncHashes != null)
                    {
                        deltaSyncHashes.TryGetValue(reefIdNormalized, out deltaSyncHash);
                    }

                    // Store the NORMALIZED ReefId (not raw) for consistency with DeltaSyncState table
                    renderedEmails.Add((recipients, subject, htmlBody, ccAddresses, attachmentConfigJson, reefIdNormalized, deltaSyncHash));

                    Log.Debug("Rendered email for approval with subject: {Subject}, ReefId: {ReefId}, Hash: {HasHash}", 
                        subject, reefId ?? "(none)", deltaSyncHash != null ? "yes" : "no");
                }
                catch (Exception ex)
                {
                    var errorMsg = $"Failed to render email: {ex.Message}";
                    errors.Add(errorMsg);
                    Log.Error(ex, errorMsg);
                }
            }

            return (renderedEmails, errors);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to render emails for approval from profile {ProfileId}", profile.Id);
            errors.Add(ex.Message);
            return (renderedEmails, errors);
        }
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

    /// <summary>
    /// Normalize ReefId value using the same logic as DeltaSyncService
    /// This ensures consistency between email approval and delta sync state
    /// </summary>
    private static string NormalizeReefId(object reefIdValue, string normalization)
    {
        if (reefIdValue == null || reefIdValue == DBNull.Value)
            return null!;

        var strValue = reefIdValue.ToString()!;

        if (normalization.Contains("Trim"))
            strValue = strValue.Trim();

        if (normalization.Contains("Lowercase"))
            strValue = strValue.ToLowerInvariant();

        if (normalization.Contains("RemoveWhitespace"))
            strValue = System.Text.RegularExpressions.Regex.Replace(strValue, @"\s+", "");

        return strValue;
    }
}
