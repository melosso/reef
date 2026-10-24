export interface JobExecution {
  id: string;
  jobId: string;
  startedAt: string;
  completedAt?: string | null;
  status: string | number;
  outputData?: string | null;
  errorMessage?: string | null;
  rowsProcessed?: number | null;
}
