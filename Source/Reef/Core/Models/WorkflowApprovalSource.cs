namespace Reef.Core.Models;

public record WorkflowApprovalSource(int WorkflowExecutionId, string NodeId, int DestinationId, string? ApproverRoles);
