export interface ImportPreviewResult {
  success: boolean;
  errorMessage?: string | null;
  totalRowsParsed: number;
  rowsReturned: number;
  columns: string[];
  rows: Record<string, unknown>[];
  parseWarnings?: string[] | null;
}
