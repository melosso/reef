export interface Job {
  id: number;
  name: string;
  description: string;
  type: string;
  profileId?: number | null;
  importProfileId?: number | null;
  profileName?: string | null;
  destinationId?: number | null;
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
