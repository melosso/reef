export interface ExecutionSplit {
  id: number;
  executionId: number;
  splitKey: string;
  rowCount: number;
  status: string;
  outputPath?: string | null;
  fileSizeBytes?: number | null;
  errorMessage?: string | null;
  startedAt: string;
  completedAt?: string | null;
}
