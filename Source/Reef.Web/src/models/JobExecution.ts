export interface JobExecution {
  id: number;
  jobId: number;
  startedAt: string;
  completedAt?: string | null;
  status: string | number;
  outputData?: string | null;
  errorMessage?: string | null;
  rowsProcessed?: number | null;
}
