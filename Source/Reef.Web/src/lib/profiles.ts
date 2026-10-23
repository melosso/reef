import { api } from './api';
import { notify } from './notify';

export type ProfileKind = 'export' | 'import';

export interface RunResult {
  status?: string;
  success?: boolean;
  error?: string;
  errorMessage?: string;
  outputPath?: string;
  rowCount?: number;
  rowsInserted?: number;
  rowsUpdated?: number;
  rowsSkipped?: number;
  rowsFailed?: number;
}

export function runOutcome(kind: ProfileKind, result: RunResult): { level: 'success' | 'warning' | 'error'; message: string } {
  const failed = /fail/i.test(result.status ?? '') || result.success === false;
  const partial = /partial/i.test(result.status ?? '');
  const counts =
    kind === 'import'
      ? `${result.rowsInserted ?? 0} inserted, ${result.rowsUpdated ?? 0} updated, ${result.rowsSkipped ?? 0} skipped, ${result.rowsFailed ?? 0} failed`
      : (result.outputPath ?? '');
  if (failed) return { level: 'error', message: `Run failed. ${result.error ?? result.errorMessage ?? counts}`.trim() };
  if (partial) return { level: 'warning', message: `Run partly succeeded. ${counts}`.trim() };
  return { level: 'success', message: `Run succeeded. ${counts}`.trim() };
}

export async function runProfile(kind: ProfileKind, id: number, name: string): Promise<RunResult> {
  const result =
    kind === 'import'
      ? await api.post<RunResult>(`/api/profiles/${id}/execute?type=import`)
      : await api.post<RunResult>(`/api/executions/execute/${id}`);
  const outcome = runOutcome(kind, result ?? {});
  notify[outcome.level](`${name}: ${outcome.message}`, { key: `run-${kind}-${id}` });
  return result ?? {};
}
