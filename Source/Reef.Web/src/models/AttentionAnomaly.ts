export interface AttentionAnomaly {
  kind: 'export' | 'import';
  profileId: number;
  profileName: string;
  executionId: number;
  reason: 'rows' | 'duration';
  value: number;
  median: number;
  startedAt: string;
}
