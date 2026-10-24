export interface ImportExecutionError {
  id: string;
  executionId: string;
  rowNumber?: number | null;
  reefId?: string | null;
  errorType: string;
  errorMessage: string;
  rowDataJson?: string | null;
  phase?: string | null;
  occurredAt: string;
}
