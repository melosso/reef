import type { AttentionAnomaly } from './AttentionAnomaly';
import type { AttentionFailure } from './AttentionFailure';
import type { AttentionJob } from './AttentionJob';

export interface DashboardAttention {
  failed: AttentionFailure[];
  anomalies: AttentionAnomaly[];
  upNext: AttentionJob[];
  last24h: { runs: number; failed: number };
}
