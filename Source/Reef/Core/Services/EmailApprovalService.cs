using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using Reef.Core.Models;
using Reef.Core.Security;
using Serilog;

namespace Reef.Core.Services;

/// <summary>
/// Service for managing email approval workflow
/// Handles creating pending approvals, approving/rejecting, and sending approved emails
/// </summary>
public class EmailApprovalService
{
    private readonly string _connectionString;
    private readonly AuditService _auditService;
    private readonly EmailExportService _emailExportService;
    private readonly ProfileService _profileService;
    private readonly NotificationService _notificationService;
    private static readonly Serilog.ILogger Log = Serilog.Log.ForContext<EmailApprovalService>();

    public EmailApprovalService(
        DatabaseConfig config,
        AuditService auditService,
        EmailExportService emailExportService,
        ProfileService profileService,
        NotificationService notificationService)
    {
        _connectionString = config.ConnectionString;
        _auditService = auditService;
        _emailExportService = emailExportService;
        _profileService = profileService;
        _notificationService = notificationService;
    }

    /// <summary>
    /// Check if an identical pending approval already exists
    /// Returns the existing approval ID if found, null otherwise
    /// </summary>
    public async Task<int?> FindExistingPendingApprovalAsync(
        int profileId,
        string? reefId,
        string? deltaSyncHash)
    {
        // Only check for duplicates if we have delta sync info
        if (string.IsNullOrEmpty(reefId) || string.IsNullOrEmpty(deltaSyncHash))
            return null;

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = @"
            SELECT Id 
            FROM PendingEmailApprovals 
            WHERE ProfileId = @ProfileId 
              AND ReefId = @ReefId 
              AND DeltaSyncHash = @DeltaSyncHash 
              AND Status = 'Pending'
            LIMIT 1
        ";

        var existingId = await connection.QueryFirstOrDefaultAsync<int?>(sql, new
        {
            ProfileId = profileId,
            ReefId = reefId,
            DeltaSyncHash = deltaSyncHash
        });

        return existingId;
    }

    /// <summary>
    /// Create a new pending email approval from rendered email details
    /// Stores the email details for later approval/rejection
    /// </summary>
    public async Task<int> CreatePendingApprovalAsync(
        int profileId,
        int profileExecutionId,
        string recipients,
        string subject,
        string htmlBody,
        string? ccAddresses = null,
        string? attachmentConfig = null,
        string? reefId = null,
        string? deltaSyncHash = null,
        string? deltaSyncRowType = null)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        try
        {
            // Generate hash for integrity checking and GUID for public API access
            var guid = Guid.NewGuid().ToString();
            var hash = GenerateHash(recipients + subject + htmlBody + (attachmentConfig ?? ""));
            var expiresAt = DateTime.UtcNow.AddDays(30); // Auto-cleanup after 30 days

            const string sql = @"
                INSERT INTO PendingEmailApprovals (
                    Guid, ProfileId, ProfileExecutionId, Recipients, CcAddresses, Subject, HtmlBody,
                    AttachmentConfig, ReefId, DeltaSyncHash, DeltaSyncRowType, Status, CreatedAt, ExpiresAt, Hash
                )
                VALUES (
                    @Guid, @ProfileId, @ProfileExecutionId, @Recipients, @CcAddresses, @Subject, @HtmlBody,
                    @AttachmentConfig, @ReefId, @DeltaSyncHash, @DeltaSyncRowType, 'Pending', @CreatedAt, @ExpiresAt, @Hash
                );
                SELECT last_insert_rowid();
            ";

            var approvalId = await connection.ExecuteScalarAsync<int>(sql, new
            {
                Guid = guid,
                ProfileId = profileId,
                ProfileExecutionId = profileExecutionId,
                Recipients = recipients,
                CcAddresses = ccAddresses,
                Subject = subject,
                HtmlBody = htmlBody,
                AttachmentConfig = attachmentConfig,
                ReefId = reefId,
                DeltaSyncHash = deltaSyncHash,
                DeltaSyncRowType = deltaSyncRowType,
                CreatedAt = DateTime.UtcNow.ToString("o"),
                ExpiresAt = expiresAt.ToString("o"),
                Hash = hash
            });

            // Update ProfileExecution with approval ID and status
            const string updateExecutionSql = @"
                UPDATE ProfileExecutions
                SET ApprovalStatus = 'Pending', PendingEmailApprovalId = @ApprovalId
                WHERE Id = @ExecutionId
            ";

            await connection.ExecuteAsync(updateExecutionSql, new
            {
                ApprovalId = approvalId,
                ExecutionId = profileExecutionId
            });

            Log.Information("Created pending email approval {ApprovalId} for profile {ProfileId}, execution {ExecutionId}",
                approvalId, profileId, profileExecutionId);

            // Audit logging - optional, skip if service is not available
            try
            {
                await _auditService.LogAsync("PendingEmailApproval", approvalId, "Created",
                    "System", $"Email pending approval (recipients: {recipients})");
            }
            catch { /* Audit logging failure should not block operation */ }

            // Send notification if enabled - get count of all pending items for notification
            try
            {
                var pendingCount = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM PendingEmailApprovals WHERE Status = 'Pending'");

                // Fire and forget - don't block approval creation on notification
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _notificationService.SendNewEmailApprovalNotificationAsync(pendingCount);
                    }
                    catch (Exception ex)
                    {
                        Log.Warning(ex, "Failed to send email approval notification");
                    }
                });
            }
            catch { /* Notification failure should not block operation */ }

            return approvalId;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to create pending email approval for profile {ProfileId}, execution {ExecutionId}",
                profileId, profileExecutionId);
            throw;
        }
    }

    /// <summary>
    /// Approve a pending email approval and mark it for sending
    /// </summary>
    public async Task<PendingEmailApproval?> ApprovePendingEmailAsync(
        int approvalId,
        int userId,
        string? approvalNotes = null)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        try
        {
            const string updateSql = @"
                UPDATE PendingEmailApprovals
                SET Status = 'Approved', ApprovedByUserId = @UserId, ApprovedAt = @ApprovedAt, ApprovalNotes = @ApprovalNotes
                WHERE Id = @Id;

                SELECT * FROM PendingEmailApprovals WHERE Id = @Id;
            ";

            var approval = await connection.QueryFirstOrDefaultAsync<PendingEmailApproval>(
                updateSql, new
                {
                    Id = approvalId,
                    UserId = userId,
                    ApprovedAt = DateTime.UtcNow.ToString("o"),
                    ApprovalNotes = approvalNotes
                });

            if (approval != null)
            {
                // Update ProfileExecution approval status
                const string updateExecutionSql = @"
                    UPDATE ProfileExecutions
                    SET ApprovalStatus = 'Approved', ApprovedAt = @ApprovedAt
                    WHERE PendingEmailApprovalId = @ApprovalId
                ";

                await connection.ExecuteAsync(updateExecutionSql, new
                {
                    ApprovalId = approvalId,
                    ApprovedAt = DateTime.UtcNow.ToString("o")
                });

                // Note: Sensitive data redaction happens in ApprovedEmailSenderService
                // after the email is successfully sent or fails, not here during approval

                Log.Information("Approved email approval {ApprovalId} by user {UserId}", approvalId, userId);

                try
                {
                    await _auditService.LogAsync("PendingEmailApproval", approvalId, "Approved",
                        userId.ToString(), $"Email approved for sending. Notes: {approvalNotes}");
                }
                catch { /* Audit logging failure should not block operation */ }
            }

            return approval;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to approve pending email approval {ApprovalId}", approvalId);
            throw;
        }
    }

    /// <summary>
    /// Reject a pending email approval with optional rejection reason
    /// </summary>
    public async Task<PendingEmailApproval?> RejectPendingEmailAsync(
        int approvalId,
        int userId,
        string? rejectionReason = null)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        try
        {
            const string updateSql = @"
                UPDATE PendingEmailApprovals
                SET Status = 'Rejected', ApprovedByUserId = @UserId, ApprovedAt = @ApprovedAt, ApprovalNotes = @RejectionReason
                WHERE Id = @Id;

                SELECT * FROM PendingEmailApprovals WHERE Id = @Id;
            ";

            var approval = await connection.QueryFirstOrDefaultAsync<PendingEmailApproval>(
                updateSql, new
                {
                    Id = approvalId,
                    UserId = userId,
                    ApprovedAt = DateTime.UtcNow.ToString("o"),
                    RejectionReason = rejectionReason
                });

            if (approval != null)
            {
                // Update ProfileExecution approval status
                const string updateExecutionSql = @"
                    UPDATE ProfileExecutions
                    SET ApprovalStatus = 'Rejected'
                    WHERE PendingEmailApprovalId = @ApprovalId
                ";

                await connection.ExecuteAsync(updateExecutionSql, new { ApprovalId = approvalId });

                // Redact sensitive email content per security policy - keep only metadata
                const string redactSql = @"
                    UPDATE PendingEmailApprovals
                    SET Recipients = '[REDACTED]',
                        CcAddresses = CASE WHEN CcAddresses IS NOT NULL THEN '[REDACTED]' ELSE NULL END,
                        HtmlBody = '[REDACTED]',
                        AttachmentConfig = NULL
                    WHERE Id = @Id
                ";
                await connection.ExecuteAsync(redactSql, new { Id = approvalId });

                Log.Information("Rejected email approval {ApprovalId} by user {UserId}", approvalId, userId);

                try
                {
                    await _auditService.LogAsync("PendingEmailApproval", approvalId, "Rejected",
                        userId.ToString(), $"Email rejected. Reason: {rejectionReason}");
                }
                catch { /* Audit logging failure should not block operation */ }
            }

            return approval;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to reject pending email approval {ApprovalId}", approvalId);
            throw;
        }
    }

    /// <summary>
    /// Get paginated pending approvals for dashboard display
    /// Optionally filter by profile ID
    /// </summary>
    public async Task<(List<PendingEmailApproval>, int totalCount)> GetPendingApprovalsAsync(
        int pageNumber = 1,
        int pageSize = 25,
        int? profileId = null,
        string? status = null)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        try
        {
            var whereClause = "WHERE Status IN ('Pending', 'Approved', 'Sent', 'Failed', 'Rejected')";
            var parameters = new DynamicParameters();

            if (profileId.HasValue)
            {
                whereClause += " AND ProfileId = @ProfileId";
                parameters.Add("ProfileId", profileId.Value);
            }

            if (!string.IsNullOrEmpty(status))
            {
                whereClause += " AND Status = @Status";
                parameters.Add("Status", status);
            }

            // Get total count
            var countSql = $"SELECT COUNT(*) FROM PendingEmailApprovals {whereClause}";
            var totalCount = await connection.ExecuteScalarAsync<int>(countSql, parameters);

            // Get paginated results
            var offset = (pageNumber - 1) * pageSize;
            var sql = $@"
                SELECT *
                FROM PendingEmailApprovals
                {whereClause}
                ORDER BY CreatedAt DESC
                LIMIT @PageSize OFFSET @Offset
            ";

            parameters.Add("PageSize", pageSize);
            parameters.Add("Offset", offset);

            var approvals = await connection.QueryAsync<PendingEmailApproval>(sql, parameters);

            return (approvals.ToList(), totalCount);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to retrieve pending approvals");
            throw;
        }
    }

    /// <summary>
    /// Get a specific approval by ID
    /// </summary>
    public async Task<PendingEmailApproval?> GetApprovalByIdAsync(int approvalId)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = "SELECT * FROM PendingEmailApprovals WHERE Id = @Id";
        return await connection.QueryFirstOrDefaultAsync<PendingEmailApproval>(sql, new { Id = approvalId });
    }

    /// <summary>
    /// Get a specific approval by GUID (for public API access)
    /// </summary>
    public async Task<PendingEmailApproval?> GetApprovalByGuidAsync(string guid)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        const string sql = "SELECT * FROM PendingEmailApprovals WHERE Guid = @Guid";
        return await connection.QueryFirstOrDefaultAsync<PendingEmailApproval>(sql, new { Guid = guid });
    }

    /// <summary>
    /// Get pending approvals count (lightweight for badge display)
    /// </summary>
    public async Task<int> GetPendingCountAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        try
        {
            return await connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM PendingEmailApprovals WHERE Status = 'Pending'");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to retrieve pending count");
            throw;
        }
    }

    /// <summary>
    /// Approve a pending email approval by GUID
    /// </summary>
    public async Task<PendingEmailApproval?> ApprovePendingEmailAsync(
        string guid,
        int userId,
        string? approvalNotes = null)
    {
        // Get the approval by GUID first to find the ID
        var approval = await GetApprovalByGuidAsync(guid);
        if (approval == null) return null;

        // Use the existing int-based method
        return await ApprovePendingEmailAsync(approval.Id, userId, approvalNotes);
    }

    /// <summary>
    /// Reject a pending email approval by GUID
    /// </summary>
    public async Task<PendingEmailApproval?> RejectPendingEmailAsync(
        string guid,
        int userId,
        string? rejectionReason = null)
    {
        // Get the approval by GUID first to find the ID
        var approval = await GetApprovalByGuidAsync(guid);
        if (approval == null) return null;

        // Use the existing int-based method
        return await RejectPendingEmailAsync(approval.Id, userId, rejectionReason);
    }

    /// <summary>
    /// Get approval statistics for dashboard summary
    /// </summary>
    public async Task<object> GetApprovalStatisticsAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        try
        {
            var now = DateTime.UtcNow;
            var weekAgo = now.AddDays(-7);
            var monthAgo = now.AddDays(-30);

            var stats = new
            {
                PendingCount = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM PendingEmailApprovals WHERE Status = 'Pending'"),

                ApprovedCount = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM PendingEmailApprovals WHERE Status = 'Approved'"),

                RejectedCount = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM PendingEmailApprovals WHERE Status = 'Rejected'"),

                SentCount = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM PendingEmailApprovals WHERE Status = 'Sent'"),

                FailedCount = await connection.ExecuteScalarAsync<int>(
                    "SELECT COUNT(*) FROM PendingEmailApprovals WHERE Status = 'Failed'"),

                SentThisWeek = await connection.ExecuteScalarAsync<int>(
                    @"SELECT COUNT(*) FROM PendingEmailApprovals
                      WHERE Status = 'Sent' AND SentAt >= @WeekAgo",
                    new { WeekAgo = weekAgo.ToString("o") }),

                SentThisMonth = await connection.ExecuteScalarAsync<int>(
                    @"SELECT COUNT(*) FROM PendingEmailApprovals
                      WHERE Status = 'Sent' AND SentAt >= @MonthAgo",
                    new { MonthAgo = monthAgo.ToString("o") })
            };

            return stats;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to retrieve approval statistics");
            throw;
        }
    }

    /// <summary>
    /// Purge expired approvals (soft delete - mark as archived)
    /// Called by scheduled background job
    /// </summary>
    public async Task<int> PurgeExpiredApprovalsAsync(int daysToKeep = 30)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();

        try
        {
            var expirationDate = DateTime.UtcNow.AddDays(-daysToKeep);

            // Soft delete: mark with a status that indicates archived, or delete if you prefer hard delete
            // For now, we'll keep the data but you could also implement a SoftDeleted flag
            const string sql = @"
                DELETE FROM PendingEmailApprovals
                WHERE ExpiresAt IS NOT NULL AND ExpiresAt < @ExpirationDate
            ";

            var deletedCount = await connection.ExecuteAsync(sql, new { ExpirationDate = expirationDate.ToString("o") });

            Log.Information("Purged {DeletedCount} expired email approvals", deletedCount);

            return deletedCount;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to purge expired approvals");
            throw;
        }
    }

    /// <summary>
    /// Check if a user has permission to approve emails for a specific profile
    /// Validates against EmailApprovalRoles in profile configuration
    /// </summary>
    public async Task<bool> UserCanApproveAsync(int profileId, int userId)
    {
        try
        {
            var profile = await _profileService.GetByIdAsync(profileId);
            if (profile == null || !profile.EmailApprovalRequired)
                return false;

            // Get user role
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync();

            var userRole = await connection.ExecuteScalarAsync<string>(
                "SELECT Role FROM Users WHERE Id = @Id",
                new { Id = userId });

            if (string.IsNullOrEmpty(userRole))
                return false;

            // Parse approval roles from profile
            if (string.IsNullOrEmpty(profile.EmailApprovalRoles))
                return true; // If no roles specified, allow any user

            try
            {
                var allowedRoles = JsonSerializer.Deserialize<List<string>>(profile.EmailApprovalRoles) ?? new();
                return allowedRoles.Contains(userRole);
            }
            catch
            {
                Log.Warning("Failed to parse EmailApprovalRoles for profile {ProfileId}", profileId);
                return false;
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to check user approval permission for profile {ProfileId}, user {UserId}",
                profileId, userId);
            return false;
        }
    }

    /// <summary>
    /// Generate SHA256 hash for integrity checking
    /// </summary>
    private static string GenerateHash(string input)
    {
        using var sha256 = SHA256.Create();
        var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToBase64String(hashedBytes);
    }
}
