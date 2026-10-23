export interface QueryPreviewResult {
  success: boolean;
  error?: string | null;
  rowCount: number;
  totalExecutionTimeMs: number;
  columns: string[];
  rows: Record<string, unknown>[];
}
