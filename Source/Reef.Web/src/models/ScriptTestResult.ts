export interface ScriptTestResult {
  success: boolean;
  exitCode: number;
  elapsedMs: number;
  stdout?: string | null;
  stderr?: string | null;
  errorMessage?: string | null;
}
