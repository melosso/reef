export interface AttentionAnomaly {
  kind: 'export' | 'import';
  profileId: string;
  profileName: string;
  executionId: string;
  reason: 'rows' | 'duration';
  value: number;
  median: number;
  startedAt: string;
}
