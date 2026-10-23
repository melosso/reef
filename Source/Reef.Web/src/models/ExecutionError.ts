export interface ExecutionError {
  id: number;
  executionId: number;
  errorType: string;
  phase?: string | null;
  detail?: string | null;
  errorMessage: string;
  occurredAt: string;
}
