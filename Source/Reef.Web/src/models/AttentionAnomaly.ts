export interface AttentionAnomaly {
  workflowId: string;
  workflowName: string;
  executionId: string;
  reason: 'rows' | 'duration';
  value: number;
  median: number;
  startedAt: string;
}
