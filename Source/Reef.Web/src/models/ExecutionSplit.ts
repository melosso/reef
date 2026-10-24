export interface ExecutionSplit {
  id: string;
  executionId: string;
  splitKey: string;
  rowCount: number;
  status: string;
  outputPath?: string | null;
  fileSizeBytes?: number | null;
  errorMessage?: string | null;
  startedAt: string;
  completedAt?: string | null;
}
