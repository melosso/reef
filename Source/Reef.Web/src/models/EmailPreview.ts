export interface EmailPreview {
  guid: string;
  status?: string;
  recipients: string;
  ccAddresses?: string | null;
  subject: string;
  htmlBody: string;
  attachmentConfig?: string | null;
}
