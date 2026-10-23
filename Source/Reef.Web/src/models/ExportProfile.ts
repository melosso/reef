export interface ExportProfile {
  id: number;
  code: string;
  name: string;
  connectionId: number;
  connectionName?: string | null;
  connectionIsActive?: boolean | null;
  groupId?: number | null;
  groupName?: string | null;
  query: string;
  scheduleType?: string | null;
  outputFormat: string;
  outputDestinationType: string;
  outputDestinationId?: number | null;
  templateId?: number | null;
  isEmailExport: boolean;
  emailTemplateId?: number | null;
  emailApprovalRequired: boolean;
  deltaSyncEnabled: boolean;
  splitEnabled: boolean;
  isEnabled: boolean;
  createdAt: string;
  updatedAt: string;
  lastExecutedAt?: string | null;
}
