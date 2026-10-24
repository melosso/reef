export interface Job {
  id: string;
  name: string;
  description: string;
  type: string;
  profileId?: string | null;
  importProfileId?: string | null;
  profileName?: string | null;
  destinationId?: string | null;
  scheduleType: string | number;
  cronExpression?: string | null;
  intervalMinutes?: number | null;
  startTime?: string | null;
  weekDays?: string | null;
  monthDay?: number | null;
  maxRetries: number;
  timeoutMinutes: number;
  priority: string | number;
  allowConcurrent: boolean;
  autoPauseEnabled: boolean;
  isEnabled: boolean;
  status: string | number;
  nextRunTime?: string | null;
  lastRunTime?: string | null;
  lastSuccessTime?: string | null;
  lastFailureTime?: string | null;
  consecutiveFailures: number;
  tags: string;
}
