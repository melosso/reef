import type { JobExecution } from '../models/JobExecution';
import type { JobOutcome } from '../models/JobOutcome';

const FINAL_OK = new Set(['Completed', '3']);
const FINAL_FAILED = new Set(['Failed', 'Cancelled', '4', '5']);

// Completed jobs are partial when the import reports PartialSuccess or the export has failed split files.
export function jobOutcome(execution: JobExecution, exportRun?: { status?: string | null; splitFailureCount?: number | null } | null): JobOutcome {
  const status = String(execution.status);
  if (FINAL_FAILED.has(status)) return 'failed';
  if (!FINAL_OK.has(status)) return 'running';

  let output: { status?: string } = {};
  try {
    output = execution.outputData ? JSON.parse(execution.outputData) : {};
  } catch {}
  if (output.status === 'PartialSuccess') return 'partial';
  if (exportRun && ((exportRun.splitFailureCount ?? 0) > 0 || /partial/i.test(exportRun.status ?? ''))) return 'partial';
  return 'success';
}
