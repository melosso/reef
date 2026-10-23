export interface ImportExecutionError {
  id: number;
  executionId: number;
  rowNumber?: number | null;
  reefId?: string | null;
  errorType: string;
  errorMessage: string;
  rowDataJson?: string | null;
  phase?: string | null;
  occurredAt: string;
}
