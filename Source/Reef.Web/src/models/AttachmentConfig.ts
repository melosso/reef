import type { ApprovalAttachment } from './ApprovalAttachment';

export interface AttachmentConfig {
  attachments?: ApprovalAttachment[];
  generatedAttachments?: ApprovalAttachment[];
  GeneratedAttachments?: ApprovalAttachment[];
  enabled?: boolean;
  Enabled?: boolean;
  mode?: string;
  Mode?: string;
}
