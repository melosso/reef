export interface AttentionFailure {
  kind: 'export' | 'import';
  profileId: number;
  profileName: string;
  executionId: number;
  error: string;
  startedAt: string;
}
