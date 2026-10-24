export interface ImportTargetResult {
  destinationId: string;
  destinationName: string;
  status: string;
  finalPath?: string | null;
  errorMessage?: string | null;
  rowsWritten: number;
}
