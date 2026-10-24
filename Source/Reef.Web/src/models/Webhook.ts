export interface Webhook {
  id: string;
  profileId?: string | null;
  jobId?: string | null;
  importProfileId?: string | null;
  token: string;
  url?: string;
  isActive: boolean;
  createdAt: string;
  lastTriggeredAt?: string | null;
  triggerCount: number;
}
