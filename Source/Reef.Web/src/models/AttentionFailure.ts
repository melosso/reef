export interface AttentionFailure {
  kind: 'export' | 'import';
  profileId: string;
  profileName: string;
  executionId: string;
  error: string;
  startedAt: string;
}
