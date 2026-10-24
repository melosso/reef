export interface ExportProfile {
  id: string;
  code: string;
  name: string;
  connectionId: string;
  connectionName?: string | null;
  connectionIsActive?: boolean | null;
  groupId?: string | null;
  groupName?: string | null;
  query: string;
  scheduleType?: string | null;
  outputFormat: string;
  outputDestinationType: string;
  outputDestinationId?: string | null;
  templateId?: string | null;
  isEmailExport: boolean;
  emailTemplateId?: string | null;
  emailApprovalRequired: boolean;
  deltaSyncEnabled: boolean;
  splitEnabled: boolean;
  isEnabled: boolean;
  createdAt: string;
  updatedAt: string;
  lastExecutedAt?: string | null;
}
