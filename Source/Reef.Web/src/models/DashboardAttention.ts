import type { AttentionAnomaly } from './AttentionAnomaly';
import type { AttentionFailure } from './AttentionFailure';
import type { AttentionJob } from './AttentionJob';
import type { AttentionWorkflow } from './AttentionWorkflow';

export interface DashboardAttention {
  failed: AttentionFailure[];
  anomalies: AttentionAnomaly[];
  upNext: AttentionJob[];
  upNextWorkflows: AttentionWorkflow[];
  last24h: { runs: number; failed: number };
}
