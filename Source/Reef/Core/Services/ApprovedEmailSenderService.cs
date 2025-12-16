using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using MimeKit;
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
        Log.Debug("ApprovedEmailSenderService polling for approved/skipped emails");

        // Give the application a moment to fully start up
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

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
                    Log.Error(ex, "Error processing approved/skipped emails, will retry");
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
            Log.Information("ApprovedEmailSenderService cancelled during operation");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "ApprovedEmailSenderService encountered a fatal error");
            throw;
        }
        finally
        {
            Log.Information("ApprovedEmailSenderService stopped");
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
        var deltaSyncService = scope.ServiceProvider.GetRequiredService<DeltaSyncService>();

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

            Log.Information("Found {Count} approved emails ready to send", emailList.Count);

            foreach (var approval in emailList)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                await SendApprovedEmailAsync(approval, connection, destinationService, auditService, deltaSyncService, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to process approved emails");
        }

        // Also process skipped emails (commit delta sync without sending)
        Log.Information("Now checking for skipped emails...");
        try
        {
            await ProcessSkippedEmailsAsync(connection, deltaSyncService, cancellationToken);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to process skipped emails");
        }
    }

    /// <summary>
    /// Process all skipped emails - commit delta sync hash without sending
    /// </summary>
    private async Task ProcessSkippedEmailsAsync(
        SqliteConnection connection,
        DeltaSyncService deltaSyncService,
        CancellationToken cancellationToken)
    {
        // Get all skipped emails that haven't been processed
        const string sql = @"
            SELECT *
            FROM PendingEmailApprovals
            WHERE Status = 'Skipped' AND SentAt IS NULL
            ORDER BY SkippedAt ASC
            LIMIT 10
        ";

        var skippedEmails = await connection.QueryAsync<PendingEmailApproval>(sql);
        var emailList = skippedEmails.ToList();

        Log.Information("Checking for skipped emails to process: found {Count}", emailList.Count);

        if (emailList.Count == 0)
        {
            return; // No skipped emails to process
        }

        Log.Information("Processing {Count} skipped emails (delta sync only)", emailList.Count);

        foreach (var approval in emailList)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            await ProcessSkippedEmailAsync(approval, connection, deltaSyncService);
        }
    }

    /// <summary>
    /// Process a single skipped email - commit delta sync without sending
    /// </summary>
    private async Task ProcessSkippedEmailAsync(
        PendingEmailApproval approval,
        SqliteConnection connection,
        DeltaSyncService deltaSyncService)
    {
        try
        {
            // Commit delta sync hash (if present)
            await CommitDeltaSyncForApprovalAsync(approval, connection, deltaSyncService, "Skipped");

            // Mark as processed (use SentAt even though we didn't send - it means "processed")
            const string updateSql = @"
                UPDATE PendingEmailApprovals
                SET SentAt = @ProcessedAt
                WHERE Id = @Id
            ";

            await connection.ExecuteAsync(updateSql, new
            {
                Id = approval.Id,
                ProcessedAt = DateTime.UtcNow.ToString("o")
            });

            // Redact email content for privacy (same as rejected emails)
            const string redactSql = @"
                UPDATE PendingEmailApprovals
                SET Recipients = '[REDACTED]',
                    CcAddresses = CASE WHEN CcAddresses IS NOT NULL THEN '[REDACTED]' ELSE NULL END,
                    HtmlBody = '[REDACTED]',
                    AttachmentConfig = NULL
                WHERE Id = @Id
            ";
            await connection.ExecuteAsync(redactSql, new { Id = approval.Id });

            Log.Information("Successfully processed skipped email {ApprovalId} (delta sync committed, email not sent)", approval.Id);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to process skipped email {ApprovalId}", approval.Id);

            // Mark as failed
            const string failSql = @"
                UPDATE PendingEmailApprovals
                SET Status = 'Failed', ErrorMessage = @ErrorMessage
                WHERE Id = @Id
            ";

            await connection.ExecuteAsync(failSql, new
            {
                Id = approval.Id,
                ErrorMessage = $"Failed to process skip: {ex.Message}"
            });
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
        DeltaSyncService deltaSyncService,
        CancellationToken cancellationToken)
    {
        int attemptCount = 0;
        Exception? lastException = null;

        while (attemptCount < _maxRetries)
        {
            try
            {
                attemptCount++;
                Log.Information("Sending approved email {ApprovalId} (attempt {Attempt}/{MaxRetries})", approval.Id, attemptCount, _maxRetries);

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

                // Commit delta sync for this successfully sent email
                await CommitDeltaSyncForApprovedEmailAsync(approval, connection, deltaSyncService);

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
    /// Send email via configured email provider (SMTP, Resend, or SendGrid)
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

        // Route to appropriate email provider
        var provider = emailConfig.EmailProvider?.ToLower() ?? "smtp";
        IEmailProvider emailProvider = provider switch
        {
            "resend" => new ResendEmailProvider(),
            "sendgrid" => new SendGridEmailProvider(),
            _ => new SmtpEmailProvider() // Default to SMTP
        };

        try
        {
            var (success, _, errorMessage) = await emailProvider.SendEmailAsync(message, emailConfig);

            if (!success)
            {
                throw new InvalidOperationException(errorMessage ?? "Failed to send email");
            }

            Log.Information("Email sent successfully to {Recipients} via {Provider}", MaskEmailForLog(approval.Recipients), provider);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to send email via {Provider}", provider);
            throw;
        }
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

    /// <summary>
    /// Commit delta sync for a successfully sent approved email
    /// This marks the email's reef_id as synced so it won't be re-sent
    /// </summary>
    private async Task CommitDeltaSyncForApprovedEmailAsync(
        PendingEmailApproval approval,
        SqliteConnection connection,
        DeltaSyncService deltaSyncService)
    {
        await CommitDeltaSyncForApprovalAsync(approval, connection, deltaSyncService, "Approved");
    }

    /// <summary>
    /// Commit delta sync hash for an approval (approved or skipped)
    /// </summary>
    private async Task CommitDeltaSyncForApprovalAsync(
        PendingEmailApproval approval,
        SqliteConnection connection,
        DeltaSyncService deltaSyncService,
        string action)
    {
        try
        {
            // Only commit delta sync if ReefId and DeltaSyncHash are present
            if (string.IsNullOrEmpty(approval.ReefId) || string.IsNullOrEmpty(approval.DeltaSyncHash))
            {
                Log.Debug("No ReefId or DeltaSyncHash for approval {ApprovalId}, skipping delta sync commit", approval.Id);
                return;
            }

            // Get profile to check if delta sync is enabled
            var profile = await connection.QueryFirstOrDefaultAsync<Profile>(
                "SELECT * FROM Profiles WHERE Id = @Id",
                new { Id = approval.ProfileId });

            if (profile == null || !profile.DeltaSyncEnabled)
            {
                Log.Debug("Delta sync not enabled for profile {ProfileId}, skipping commit", approval.ProfileId);
                return;
            }

            // Create a minimal DeltaSyncResult with just this one reef_id
            // The hash was calculated during execution and stored with the approval
            var deltaSyncResult = new DeltaSyncResult
            {
                NewRows = new List<Dictionary<string, object>>(),
                ChangedRows = new List<Dictionary<string, object>>(),
                UnchangedRows = new List<Dictionary<string, object>>(),
                DeletedReefIds = new List<string>(),
                TotalRowsProcessed = 1,
                NewHashState = new Dictionary<string, string>
                {
                    [approval.ReefId] = approval.DeltaSyncHash
                }
            };

            Log.Information("Committing delta sync for {Action} approval {ApprovalId}: ReefId='{ReefId}', Hash='{Hash}', ExecutionId={ExecutionId}",
                action, approval.Id, approval.ReefId, approval.DeltaSyncHash, approval.ProfileExecutionId);

            // Commit the delta sync using the execution ID from the approval
            await deltaSyncService.CommitDeltaSyncAsync(
                approval.ProfileId,
                approval.ProfileExecutionId,
                deltaSyncResult);

            // Update delta sync metrics on the execution record based on row type
            await UpdateDeltaSyncMetricsForApprovalAsync(
                approval.ProfileExecutionId,
                approval.DeltaSyncRowType,
                connection);

            Log.Information("Delta sync committed for {Action} email {ApprovalId}, ReefId: {ReefId}, RowType: {RowType}",
                action, approval.Id, approval.ReefId, approval.DeltaSyncRowType ?? "(unknown)");
        }
        catch (Exception ex)
        {
            // Delta sync commit failure should not block processing
            // Log warning but don't throw
            Log.Warning(ex, "Failed to commit delta sync for {Action} email {ApprovalId}, ReefId: {ReefId}",
                action, approval.Id, approval.ReefId);
        }
    }

    /// <summary>
    /// Update delta sync metrics incrementally as each approved email is sent
    /// This increments the counters on the ProfileExecution record based on row type
    /// </summary>
    private async Task UpdateDeltaSyncMetricsForApprovalAsync(
        int executionId,
        string? rowType,
        SqliteConnection connection)
    {
        try
        {
            if (string.IsNullOrEmpty(rowType))
            {
                Log.Debug("No row type for execution {ExecutionId}, skipping metrics update", executionId);
                return;
            }

            // Increment the appropriate counter based on row type
            string sql;
            if (rowType == "New")
            {
                sql = @"
                    UPDATE ProfileExecutions 
                    SET DeltaSyncNewRows = COALESCE(DeltaSyncNewRows, 0) + 1,
                        DeltaSyncTotalHashedRows = COALESCE(DeltaSyncTotalHashedRows, 0) + 1
                    WHERE Id = @Id";
            }
            else if (rowType == "Changed")
            {
                sql = @"
                    UPDATE ProfileExecutions 
                    SET DeltaSyncChangedRows = COALESCE(DeltaSyncChangedRows, 0) + 1,
                        DeltaSyncTotalHashedRows = COALESCE(DeltaSyncTotalHashedRows, 0) + 1
                    WHERE Id = @Id";
            }
            else if (rowType == "Deleted")
            {
                sql = @"
                    UPDATE ProfileExecutions 
                    SET DeltaSyncDeletedRows = COALESCE(DeltaSyncDeletedRows, 0) + 1,
                        DeltaSyncTotalHashedRows = COALESCE(DeltaSyncTotalHashedRows, 0) + 1
                    WHERE Id = @Id";
            }
            else
            {
                Log.Debug("Unknown row type '{RowType}' for execution {ExecutionId}, skipping metrics update", 
                    rowType, executionId);
                return;
            }

            await connection.ExecuteAsync(sql, new { Id = executionId });

            Log.Debug("Updated delta sync metrics for execution {ExecutionId}, RowType: {RowType}",
                executionId, rowType);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to update delta sync metrics for execution {ExecutionId}", executionId);
        }
    }
}
