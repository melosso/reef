export interface NotificationTemplate {
  templateType: string;
  subject?: string | null;
  htmlBody?: string | null;
  ctaButtonText?: string | null;
  ctaUrlOverride?: string | null;
  isDefault?: boolean;
}
