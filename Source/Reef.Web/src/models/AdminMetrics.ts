import type { ExecutionStats } from './ExecutionStats';

export interface AdminMetrics {
  users?: number;
  apikeys?: number;
  connections?: number;
  workflows?: { total?: number; active?: number };
  executions?: { total?: number; successRate?: number; failedLast24h?: number };
  notifications?: { isEnabled?: boolean };
  recentStats?: { last7Days?: ExecutionStats };
  system?: { databaseSizeMb?: number; databaseSizeThresholdMb?: number; exportsSizeMb?: number; uptimeDays?: number };
}
