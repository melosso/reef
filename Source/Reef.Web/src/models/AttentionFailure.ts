export interface AttentionFailure {
  workflowId: string;
  workflowName: string;
  executionId: string;
  error: string;
  startedAt: string;
}
