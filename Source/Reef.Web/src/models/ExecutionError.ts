export interface ExecutionError {
  id: string;
  executionId: string;
  errorType: string;
  phase?: string | null;
  detail?: string | null;
  errorMessage: string;
  occurredAt: string;
}
