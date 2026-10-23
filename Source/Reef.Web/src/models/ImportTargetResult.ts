export interface ImportTargetResult {
  destinationId: number;
  destinationName: string;
  status: string;
  finalPath?: string | null;
  errorMessage?: string | null;
  rowsWritten: number;
}
