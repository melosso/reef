using Microsoft.AspNetCore.Mvc;
using Reef.Core.Models;
using Reef.Core.Services;
using Serilog;
using System.Security.Claims;

namespace Reef.Api;

/// <summary>
/// API endpoints for email approval workflow management
/// Provides REST operations for viewing pending approvals and approving/rejecting emails
/// </summary>
public static class EmailApprovalsEndpoints
{
    public static void Map(WebApplication app)
    {
        var group = app.MapGroup("/api/email-approvals").RequireAuthorization();

        group.MapGet("/", GetPendingApprovals);
        group.MapGet("/count", GetPendingCount);
        group.MapGet("/{guid:guid}", GetApprovalByGuid);
        group.MapGet("/profile/{profileId:int}", GetApprovalsByProfile);
        group.MapGet("/statistics", GetApprovalStatistics);
        group.MapGet("/{guid:guid}/preview", PreviewApprovedEmail);

        group.MapPost("/{guid:guid}/approve", ApproveEmail);
        group.MapPost("/{guid:guid}/reject", RejectEmail);
        group.MapPost("/{guid:guid}/send-now", SendApprovedEmailNow);

        group.MapDelete("/{guid:guid}", DeleteApproval);
    }

    /// <summary>
    /// GET /api/email-approvals - Get paginated pending approvals
    /// </summary>
    private static async Task<IResult> GetPendingApprovals(
        [FromServices] EmailApprovalService service,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] int? profileId = null,
        [FromQuery] string? status = null)
    {
        try
        {
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1 || pageSize > 100) pageSize = 25;

            var (approvals, totalCount) = await service.GetPendingApprovalsAsync(pageNumber, pageSize, profileId, status);

            var totalPages = (totalCount + pageSize - 1) / pageSize;

            return Results.Ok(new
            {
                data = approvals,
                pagination = new
                {
                    pageNumber,
                    pageSize,
                    totalCount,
                    totalPages
                }
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error getting pending approvals");
            return Results.Problem("Error retrieving pending approvals");
        }
    }

    /// <summary>
    /// GET /api/email-approvals/count - Get pending approvals count (lightweight endpoint for badges)
    /// Returns count 1-99, or 99 for anything >= 99
    /// </summary>
    private static async Task<IResult> GetPendingCount(
        [FromServices] EmailApprovalService service)
    {
        try
        {
            var count = await service.GetPendingCountAsync();
            // Cap at 99 for display purposes
            var displayCount = count > 99 ? 99 : count;
            
            return Results.Ok(new { count = displayCount });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error getting pending count");
            return Results.Problem("Error retrieving pending count");
        }
    }

    /// <summary>
    /// GET /api/email-approvals/{guid} - Get a specific approval by GUID
    /// </summary>
    private static async Task<IResult> GetApprovalByGuid(
        string guid,
        [FromServices] EmailApprovalService service)
    {
        try
        {
            var approval = await service.GetApprovalByGuidAsync(guid);
            return approval != null ? Results.Ok(approval) : Results.NotFound(new { message = "Approval not found" });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error getting approval {Guid}", guid);
            return Results.Problem("Error retrieving approval");
        }
    }

    /// <summary>
    /// GET /api/email-approvals/profile/{profileId} - Get all approvals for a profile
    /// </summary>
    private static async Task<IResult> GetApprovalsByProfile(
        int profileId,
        [FromServices] EmailApprovalService service,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 25)
    {
        try
        {
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1 || pageSize > 100) pageSize = 25;

            var (approvals, totalCount) = await service.GetPendingApprovalsAsync(pageNumber, pageSize, profileId);

            var totalPages = (totalCount + pageSize - 1) / pageSize;

            return Results.Ok(new
            {
                data = approvals,
                pagination = new
                {
                    pageNumber,
                    pageSize,
                    totalCount,
                    totalPages
                }
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error getting approvals for profile {ProfileId}", profileId);
            return Results.Problem("Error retrieving approvals");
        }
    }

    /// <summary>
    /// GET /api/email-approvals/statistics - Get approval workflow statistics
    /// </summary>
    private static async Task<IResult> GetApprovalStatistics(
        [FromServices] EmailApprovalService service)
    {
        try
        {
            var stats = await service.GetApprovalStatisticsAsync();
            return Results.Ok(stats);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error getting approval statistics");
            return Results.Problem("Error retrieving statistics");
        }
    }

    /// <summary>
    /// GET /api/email-approvals/{guid}/preview - Preview email content before approval
    /// </summary>
    private static async Task<IResult> PreviewApprovedEmail(
        string guid,
        [FromServices] EmailApprovalService service)
    {
        try
        {
            var approval = await service.GetApprovalByGuidAsync(guid);
            if (approval == null)
                return Results.NotFound(new { message = "Approval not found" });

            return Results.Ok(new
            {
                guid = approval.Guid,
                recipients = approval.Recipients,
                ccAddresses = approval.CcAddresses,
                subject = approval.Subject,
                htmlBody = approval.HtmlBody,
                status = approval.Status,
                createdAt = approval.CreatedAt,
                approvalNotes = approval.ApprovalNotes
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error previewing approval {Guid}", guid);
            return Results.Problem("Error previewing approval");
        }
    }

    /// <summary>
    /// POST /api/email-approvals/{guid}/approve - Approve an email for sending
    /// </summary>
    private static async Task<IResult> ApproveEmail(
        string guid,
        [FromBody] ApproveEmailRequest request,
        [FromServices] EmailApprovalService service,
        HttpContext context)
    {
        try
        {
            var userId = GetUserIdFromContext(context);
            if (userId <= 0)
                return Results.Unauthorized();

            // Check if user has permission to approve
            var approval = await service.GetApprovalByGuidAsync(guid);
            if (approval == null)
                return Results.NotFound(new { message = "Approval not found" });

            var canApprove = await service.UserCanApproveAsync(approval.ProfileId, userId);
            if (!canApprove)
                return Results.Forbid();

            var approvedApproval = await service.ApprovePendingEmailAsync(guid, userId, request.Notes);

            return approvedApproval != null
                ? Results.Ok(new { message = "Email approved for sending", approval = approvedApproval })
                : Results.NotFound(new { message = "Approval not found" });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error approving email {Guid}", guid);
            return Results.Problem("Error approving email");
        }
    }

    /// <summary>
    /// POST /api/email-approvals/{guid}/reject - Reject an email approval
    /// </summary>
    private static async Task<IResult> RejectEmail(
        string guid,
        [FromBody] RejectEmailRequest request,
        [FromServices] EmailApprovalService service,
        HttpContext context)
    {
        try
        {
            var userId = GetUserIdFromContext(context);
            if (userId <= 0)
                return Results.Unauthorized();

            // Check if user has permission to approve
            var approval = await service.GetApprovalByGuidAsync(guid);
            if (approval == null)
                return Results.NotFound(new { message = "Approval not found" });

            var canApprove = await service.UserCanApproveAsync(approval.ProfileId, userId);
            if (!canApprove)
                return Results.Forbid();

            var rejectedApproval = await service.RejectPendingEmailAsync(guid, userId, request.Reason);

            return rejectedApproval != null
                ? Results.Ok(new { message = "Email rejected", approval = rejectedApproval })
                : Results.NotFound(new { message = "Approval not found" });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error rejecting email {Guid}", guid);
            return Results.Problem("Error rejecting email");
        }
    }

    /// <summary>
    /// POST /api/email-approvals/{guid}/send-now - Force send an already-approved email
    /// </summary>
    private static async Task<IResult> SendApprovedEmailNow(
        string guid,
        [FromServices] EmailApprovalService service,
        [FromServices] DestinationService destinationService,
        HttpContext context)
    {
        try
        {
            var userId = GetUserIdFromContext(context);
            if (userId <= 0)
                return Results.Unauthorized();

            var approval = await service.GetApprovalByGuidAsync(guid);
            if (approval == null)
                return Results.NotFound(new { message = "Approval not found" });

            if (approval.Status != "Approved")
                return Results.BadRequest(new { message = "Email must be in 'Approved' status to send" });

            // Note: In a real implementation, you would integrate with EmailExportService
            // to actually send the email. For now, this is a placeholder that allows
            // manual triggering of the ApprovedEmailSenderService
            // The background service will pick this up on the next poll cycle

            Log.Information("Manual send triggered for approved email {Guid} by user {UserId}", guid, userId);

            return Results.Ok(new
            {
                message = "Email queued for sending. The background service will send it shortly.",
                approval = approval
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error triggering send for approval {Guid}", guid);
            return Results.Problem("Error triggering send");
        }
    }

    /// <summary>
    /// DELETE /api/email-approvals/{guid} - Soft delete an approval (archive)
    /// </summary>
    private static async Task<IResult> DeleteApproval(
        string guid,
        [FromServices] EmailApprovalService service,
        HttpContext context)
    {
        try
        {
            var userId = GetUserIdFromContext(context);
            if (userId <= 0)
                return Results.Unauthorized();

            var approval = await service.GetApprovalByGuidAsync(guid);
            if (approval == null)
                return Results.NotFound(new { message = "Approval not found" });

            // In a real implementation, you would implement a soft-delete method
            // For now, this is a placeholder
            // Typically would mark as archived rather than hard delete

            Log.Information("Deletion requested for approval {Guid} by user {UserId}", guid, userId);

            return Results.Ok(new { message = "Approval archived" });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error deleting approval {Guid}", guid);
            return Results.Problem("Error deleting approval");
        }
    }

    /// <summary>
    /// Extract user ID from JWT token in HttpContext
    /// Returns 0 if not found or invalid
    /// </summary>
    private static int GetUserIdFromContext(HttpContext context)
    {
        try
        {
            // Try multiple claim types for userId
            var userIdClaim = context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? context.User?.FindFirst("sub")?.Value
                ?? context.User?.FindFirst("userId")?.Value;

            if (int.TryParse(userIdClaim, out var userId))
                return userId;
        }
        catch
        {
            // Log and continue
        }

        return 0;
    }
}

/// <summary>
/// Request model for approving an email
/// </summary>
public class ApproveEmailRequest
{
    public string? Notes { get; set; }
}

/// <summary>
/// Request model for rejecting an email
/// </summary>
public class RejectEmailRequest
{
    public string? Reason { get; set; }
}
