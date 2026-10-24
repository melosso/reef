export interface EmailApproval {
  id: string;
  guid: string;
  profileId: string;
  profileExecutionId: string;
  recipients: string;
  ccAddresses?: string | null;
  subject: string;
  htmlBody: string;
  attachmentConfig?: string | null;
  status: string;
  approvedAt?: string | null;
  skippedAt?: string | null;
  approvalNotes?: string | null;
  createdAt: string;
  expiresAt?: string | null;
  errorMessage?: string | null;
  sentAt?: string | null;
}
