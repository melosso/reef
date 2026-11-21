export interface NodeRun {
  id: string;
  nodeId: string;
  iteration: number;
  status: string;
  startedAt: string;
  completedAt?: string | null;
  rowCount: number;
  outputSampleJson?: string | null;
  errorMessage?: string | null;
}
