export interface ConnectionTestResult {
  success: boolean;
  message?: string | null;
  responseTimeMs?: number | null;
}
