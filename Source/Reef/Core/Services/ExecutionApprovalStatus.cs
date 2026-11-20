namespace Reef.Core.Services;

// A run holds several emails, its approval status is derived from all of them after every change.
public static class ExecutionApprovalStatus
{
    public const string RefreshForApprovalSql = """
        UPDATE ProfileExecutions
        SET ApprovalStatus = (
            SELECT CASE
                WHEN SUM(Status = 'Pending') > 0 THEN 'Pending'
                WHEN SUM(Status = 'Approved') > 0 THEN 'Approved'
                WHEN SUM(Status = 'Failed') > 0 THEN 'Failed'
                WHEN SUM(Status = 'Sent') > 0 THEN 'Sent'
                WHEN SUM(Status = 'Rejected') > 0 THEN 'Rejected'
                ELSE 'Skipped' END
            FROM PendingEmailApprovals WHERE ProfileExecutionId = ProfileExecutions.Id)
        WHERE Id = (SELECT ProfileExecutionId FROM PendingEmailApprovals WHERE Id = @ApprovalId)
        """;
}
