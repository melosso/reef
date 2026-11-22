import type { JobExecution } from '../models/JobExecution';
import type { JobOutcome } from '../models/JobOutcome';

const FINAL_OK = new Set(['Completed', '3']);
const FINAL_FAILED = new Set(['Failed', 'Cancelled', '4', '5']);

// PartialSuccess output marks a partial job
export function jobOutcome(execution: JobExecution): JobOutcome {
  const status = String(execution.status);
  if (FINAL_FAILED.has(status)) return 'failed';
  if (!FINAL_OK.has(status)) return 'running';

  let output: { status?: string } = {};
  try {
    output = execution.outputData ? JSON.parse(execution.outputData) : {};
  } catch {}
  if (output.status === 'PartialSuccess') return 'partial';
  return 'success';
}
