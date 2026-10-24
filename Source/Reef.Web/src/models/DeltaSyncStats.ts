export interface DeltaSyncStats {
  enabled: boolean;
  message?: string | null;
  activeRows: number;
  deletedRows: number;
  newRowsLastRun?: number | null;
  changedRowsLastRun?: number | null;
  unchangedRowsLastRun?: number | null;
}
