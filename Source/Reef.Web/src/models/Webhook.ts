export interface Webhook {
  id: number;
  profileId?: number | null;
  jobId?: number | null;
  importProfileId?: number | null;
  token: string;
  url?: string;
  isActive: boolean;
  createdAt: string;
  lastTriggeredAt?: string | null;
  triggerCount: number;
}
