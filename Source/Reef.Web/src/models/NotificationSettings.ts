export interface NotificationSettings {
  id?: number;
  isEnabled: boolean;
  destinationId: number | string | null;
  recipientEmails?: string | null;
  notifyOnProfileFailure: boolean;
  notifyOnProfileSuccess: boolean;
  notifyOnImportProfileFailure: boolean;
  notifyOnImportProfileSuccess: boolean;
  notifyOnJobFailure: boolean;
  notifyOnJobSuccess: boolean;
  notifyOnDatabaseSizeThreshold: boolean;
  notifyOnNewApiKey: boolean;
  notifyOnNewUser: boolean;
  notifyOnNewWebhook: boolean;
  notifyOnNewEmailApproval: boolean;
  newEmailApprovalCooldownHours: number;
  databaseSizeThresholdBytes?: number;
  enableCTA: boolean;
  ctaUrl?: string | null;
  createdAt?: string;
}
