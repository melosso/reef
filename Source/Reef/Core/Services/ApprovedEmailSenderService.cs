using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using MimeKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Reef.Core.Models;
using Reef.Core.Destinations;
using Serilog;

namespace Reef.Core.Services;

/// <summary>
/// Background service that polls for approved emails and sends them
/// Runs periodically to process emails that have been approved via the approval workflow
/// </summary>
public class ApprovedEmailSenderService : BackgroundService
{
    private readonly string _connectionString;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private static readonly Serilog.ILogger Log = Serilog.Log.ForContext<ApprovedEmailSenderService>();

    private readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(30); // Poll every 30 seconds
    private readonly int _maxRetries = 3; // Retry failed sends 3 times
    private readonly TimeSpan _retryDelay = TimeSpan.FromSeconds(5); // Wait 5 seconds between retries

    public ApprovedEmailSenderService(
        DatabaseConfig config,
        IServiceScopeFactory serviceScopeFactory)
    {
        _connectionString = config.ConnectionString;
        _serviceScopeFactory = serviceScopeFactory;
    }

    /// <summary>
    /// Main background service execution loop
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Log.Debug("ApprovedEmailSenderService started");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessApprovedEmailsAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error processing approved emails, will retry");
                }

                // Wait before next poll
                try
                {
                    await Task.Delay(_pollInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // Service is stopping
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation during shutdown - don't log as error
            Log.Debug("ApprovedEmailSenderService cancelled during operation");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "ApprovedEmailSenderService encountered a fatal error");
            throw;
        }
        finally
        {
            Log.Debug("ApprovedEmailSenderService stopped");
        }
    }

    /// <summary>
    /// Process all approved emails that haven't been sent yet
    /// </summary>
    private async Task ProcessApprovedEmailsAsync(CancellationToken cancellationToken)
    {
        // Create a scope to resolve scoped services
        using var scope = _serviceScopeFactory.CreateScope();
        var destinationService = scope.ServiceProvider.GetRequiredService<DestinationService>();
        var auditService = scope.ServiceProvider.GetRequiredService<AuditService>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        try
        {
            // Get all approved emails that haven't been sent
            const string sql = @"
                SELECT *
                FROM PendingEmailApprovals
                WHERE Status = 'Approved' AND SentAt IS NULL
                ORDER BY ApprovedAt ASC
                LIMIT 10
            ";

            var approvedEmails = await connection.QueryAsync<PendingEmailApproval>(sql);
            var emailList = approvedEmails.ToList();

            if (emailList.Count == 0)
            {
                return; // No approved emails to send
            }

            Log.Debug("Found {Count} approved emails to send", emailList.Count);

            foreach (var approval in emailList)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                await SendApprovedEmailAsync(approval, connection, destinationService, auditService, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to process approved emails");
        }
    }

    /// <summary>
    /// Send a single approved email with retry logic
    /// </summary>
    private async Task SendApprovedEmailAsync(
        PendingEmailApproval approval,
        SqliteConnection connection,
        DestinationService destinationService,
        AuditService auditService,
        CancellationToken cancellationToken)
    {
        int attemptCount = 0;
        Exception? lastException = null;

        while (attemptCount < _maxRetries)
        {
            try
            {
                attemptCount++;
                Log.Debug("Sending approved email {ApprovalId} (attempt {Attempt}/{MaxRetries})", approval.Id, attemptCount, _maxRetries);

                // Get email destination from ProfileExecutions -> Profiles
                var profileId = approval.ProfileId;

                // Load the email destination (we need to query Profiles to get OutputDestinationId)
                var destinationId = await connection.ExecuteScalarAsync<int?>(
                    "SELECT OutputDestinationId FROM Profiles WHERE Id = @ProfileId",
                    new { ProfileId = profileId });

                if (!destinationId.HasValue)
                {
                    throw new InvalidOperationException($"Email destination not configured for profile {profileId}");
                }

                var emailDestination = await destinationService.GetByIdForExecutionAsync(destinationId.Value);
                if (emailDestination == null || emailDestination.Type != DestinationType.Email)
                {
                    throw new InvalidOperationException($"Email destination {destinationId} not found or not type Email");
                }

                var emailConfig = JsonSerializer.Deserialize<EmailDestinationConfiguration>(emailDestination.ConfigurationJson);
                if (emailConfig == null)
                {
                    throw new InvalidOperationException("Invalid email destination configuration");
                }

                // Send the email
                await SendEmailAsync(approval, emailConfig, cancellationToken);

                // Mark as sent
                const string updateSql = @"
                    UPDATE PendingEmailApprovals
                    SET Status = 'Sent', SentAt = @SentAt
                    WHERE Id = @Id
                ";

                await connection.ExecuteAsync(updateSql, new
                {
                    Id = approval.Id,
                    SentAt = DateTime.UtcNow.ToString("o")
                });

                // Update ProfileExecution ApprovalStatus to reflect successful email send
                const string updateExecutionSql = @"
                    UPDATE ProfileExecutions
                    SET ApprovalStatus = 'Sent'
                    WHERE PendingEmailApprovalId = @ApprovalId
                ";

                await connection.ExecuteAsync(updateExecutionSql, new
                {
                    ApprovalId = approval.Id
                });

                // Redact sensitive email content per security policy - keep only metadata
                const string redactSql = @"
                    UPDATE PendingEmailApprovals
                    SET Recipients = '[REDACTED]',
                        CcAddresses = CASE WHEN CcAddresses IS NOT NULL THEN '[REDACTED]' ELSE NULL END,
                        HtmlBody = '[REDACTED]',
                        AttachmentConfig = NULL
                    WHERE Id = @Id
                ";
                await connection.ExecuteAsync(redactSql, new { Id = approval.Id });

                Log.Information("Successfully sent approved email {ApprovalId} to {Recipients}", approval.Id, MaskEmailForLog(approval.Recipients));

                try
                {
                    await auditService.LogAsync("PendingEmailApproval", approval.Id, "Sent",
                        "ApprovedEmailSenderService", $"Email successfully sent on attempt {attemptCount}");
                }
                catch { /* Audit logging failure should not block operation */ }

                return; // Success!
            }
            catch (Exception ex)
            {
                lastException = ex;
                Log.Warning(ex, "Failed to send approved email {ApprovalId} (attempt {Attempt}/{MaxRetries})", approval.Id, attemptCount, _maxRetries);

                if (attemptCount < _maxRetries)
                {
                    // Wait before retry
                    await Task.Delay(_retryDelay, cancellationToken);
                }
            }
        }

        // All retries exhausted - mark as failed
        const string failSql = @"
            UPDATE PendingEmailApprovals
            SET Status = 'Failed', ErrorMessage = @ErrorMessage
            WHERE Id = @Id
        ";

        await connection.ExecuteAsync(failSql, new
        {
            Id = approval.Id,
            ErrorMessage = $"Failed after {_maxRetries} attempts: {lastException?.Message}"
        });

        // Update ProfileExecution ApprovalStatus to reflect email send failure
        // Keep Status as 'Success' since the data extraction succeeded - only the email delivery failed
        const string updateExecutionFailedSql = @"
            UPDATE ProfileExecutions
            SET ApprovalStatus = 'Failed'
            WHERE PendingEmailApprovalId = @ApprovalId
        ";

        await connection.ExecuteAsync(updateExecutionFailedSql, new
        {
            ApprovalId = approval.Id
        });

        Log.Error("Failed to send approved email {ApprovalId} after {Retries} attempts: {Error}",
            approval.Id, _maxRetries, lastException?.Message);

        // Redact sensitive email content per security policy - keep only metadata
        const string redactFailedSql = @"
            UPDATE PendingEmailApprovals
            SET Recipients = '[REDACTED]',
                CcAddresses = CASE WHEN CcAddresses IS NOT NULL THEN '[REDACTED]' ELSE NULL END,
                HtmlBody = '[REDACTED]',
                AttachmentConfig = NULL
            WHERE Id = @Id
        ";
        await connection.ExecuteAsync(redactFailedSql, new { Id = approval.Id });

        try
        {
            await auditService.LogAsync("PendingEmailApproval", approval.Id, "FailedToSend",
                "ApprovedEmailSenderService", $"Email failed to send after {_maxRetries} attempts: {lastException?.Message}");
        }
        catch { /* Audit logging failure should not block operation */ }
    }

    /// <summary>
    /// Send email via configured SMTP server
    /// </summary>
    private async Task SendEmailAsync(PendingEmailApproval approval, EmailDestinationConfiguration emailConfig, CancellationToken cancellationToken)
    {
        var message = new MimeMessage();

        // Parse and set sender
        var fromEmail = string.IsNullOrEmpty(emailConfig.FromAddress)
            ? emailConfig.Username ?? "noreply@example.com"
            : emailConfig.FromAddress;

        try
        {
            message.From.Add(MailboxAddress.Parse(fromEmail));
        }
        catch
        {
            message.From.Add(new MailboxAddress("Reef Export Service", "noreply@example.com"));
        }

        // Parse and add recipients
        var recipients = approval.Recipients.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var recipient in recipients)
        {
            try
            {
                var trimmedRecipient = recipient.Trim();
                message.To.Add(MailboxAddress.Parse(trimmedRecipient));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to parse recipient email: {Recipient}", recipient);
            }
        }

        // Add CC if present
        if (!string.IsNullOrEmpty(approval.CcAddresses))
        {
            var ccAddresses = approval.CcAddresses.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var cc in ccAddresses)
            {
                try
                {
                    message.Cc.Add(MailboxAddress.Parse(cc.Trim()));
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to parse CC email: {Cc}", cc);
                }
            }
        }

        // Set subject and body
        message.Subject = approval.Subject;
        var bodyBuilder = new BodyBuilder { HtmlBody = approval.HtmlBody };
        message.Body = bodyBuilder.ToMessageBody();

        // Connect to SMTP and send
        var smtpHost = emailConfig.SmtpServer ?? "localhost";
        var smtpPort = emailConfig.SmtpPort > 0 ? emailConfig.SmtpPort : 587;

        using var client = new SmtpClient();

        try
        {
            // Set timeout
            client.Timeout = 10000; // 10 seconds

            // Connect to SMTP
            var secureSocketOptions = GetSecureSocketOptions(emailConfig.SecurityMode);
            await client.ConnectAsync(smtpHost, smtpPort, secureSocketOptions, cancellationToken);

            // Authenticate if needed
            if (!string.IsNullOrEmpty(emailConfig.Username) && !string.IsNullOrEmpty(emailConfig.Password))
            {
                await client.AuthenticateAsync(emailConfig.Username, emailConfig.Password, cancellationToken);
            }

            // Send
            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            Log.Information("Email sent successfully to {Recipients}", MaskEmailForLog(approval.Recipients));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to send email via SMTP");
            throw;
        }
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
            _ => SecureSocketOptions.StartTls // Default
        };
    }

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
}
