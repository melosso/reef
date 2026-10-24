import { api } from '../lib/api';
import { formatBytes, formatNumber, parseDate } from '../lib/format';
import { statusKind } from '../lib/status';
import type { StatusKind } from '../components/StatusMark.svelte';
import type { ExportExecution } from '../models/ExportExecution';
import type { ImportExecution } from '../models/ImportExecution';

export interface StepStat {
  text: string;
  ms?: number | null;
  status?: StatusKind;
}

export type LastRun = { kind: 'export'; run: ExportExecution } | { kind: 'import'; run: ImportExecution };

export async function loadLastRun(kind: 'export' | 'import', id: string): Promise<LastRun | null> {
  if (kind === 'import') {
    const list = await api.get<ImportExecution[]>(`/api/profiles/${id}/executions?type=import&limit=1`).catch(() => []);
    return list?.[0] ? { kind, run: list[0] } : null;
  }
  const list = await api.get<ExportExecution[]>(`/api/executions/by-profile/${id}?limit=1`).catch(() => []);
  return list?.[0] ? { kind, run: list[0] } : null;
}

const rows = (n: number | null | undefined, word = 'rows') => `${formatNumber(n ?? 0)} ${word}`;

function exportStat(type: string, e: ExportExecution): StepStat | null {
  switch (type) {
    case 'preprocess':
      return e.preProcessStatus ? { text: e.preProcessStatus, ms: e.preProcessTimeMs, status: statusKind(e.preProcessStatus) } : null;
    case 'postprocess':
      return e.postProcessStatus ? { text: e.postProcessStatus, ms: e.postProcessTimeMs, status: statusKind(e.postProcessStatus) } : null;
    case 'source':
      return { text: rows(e.rowCount), status: statusKind(e.status) };
    case 'deltasync':
      return e.deltaSyncTotalHashedRows || e.deltaSyncNewRows || e.deltaSyncChangedRows
        ? { text: `${formatNumber(e.deltaSyncNewRows ?? 0)} new, ${formatNumber(e.deltaSyncChangedRows ?? 0)} changed` }
        : null;
    case 'splitoutput':
      return e.wasSplit ? { text: `${formatNumber(e.splitSuccessCount ?? 0)} of ${formatNumber(e.splitCount ?? 0)} files` } : null;
    case 'destination':
      return {
        text: e.fileSizeBytes
          ? formatBytes(e.fileSizeBytes)
          : String(e.outputPath ?? '')
              .split(/[\\/]/)
              .pop() || 'No file',
        ms: e.executionTimeMs,
        status: statusKind(e.status),
      };
    case 'emailexport':
      return {
        text: e.approvalStatus ? `Approval ${String(e.approvalStatus).toLowerCase()}` : rows(e.rowCount, 'rows sent'),
        ms: e.executionTimeMs,
        status: statusKind(e.status),
      };
    default:
      return null;
  }
}

function importStat(type: string, e: ImportExecution): StepStat | null {
  let phases: Record<string, number> = {};
  try {
    phases = e.phaseTimingsJson ? JSON.parse(e.phaseTimingsJson) : {};
  } catch {}
  const sum = (...keys: string[]) => (keys.some((k) => phases[k] !== undefined) ? keys.reduce((t, k) => t + (phases[k] ?? 0), 0) : null);
  switch (type) {
    case 'preprocess':
      return phases.PreProcess !== undefined ? { text: 'Ran', ms: phases.PreProcess } : null;
    case 'source':
      return { text: rows(e.filesProcessed, 'files'), ms: sum('FetchSource') };
    case 'parse':
      return { text: rows(e.totalRowsRead), ms: sum('GetSchema', 'ParseAndWrite') };
    case 'deltasync':
      return e.deltaSyncNewRows || e.deltaSyncChangedRows || e.deltaSyncUnchangedRows
        ? {
            text: `${formatNumber(e.deltaSyncNewRows ?? 0)} new, ${formatNumber(e.deltaSyncChangedRows ?? 0)} changed`,
            ms: sum('LoadDeltaState', 'CommitDeltaState'),
          }
        : null;
    case 'target':
      return {
        text: `${formatNumber(e.rowsInserted ?? 0)} inserted, ${formatNumber(e.rowsUpdated ?? 0)} updated`,
        ms: sum('FullReplace', 'ApplyDeletes'),
        status: statusKind(e.status),
      };
    case 'outputs':
      return phases.FanOut !== undefined ? { text: 'Copied', ms: phases.FanOut } : null;
    case 'postprocess':
      return phases.PostProcess !== undefined ? { text: 'Ran', ms: phases.PostProcess } : null;
    default:
      return null;
  }
}

export function stepStat(last: LastRun | null, type: string): StepStat | null {
  if (!last) return null;
  return last.kind === 'import' ? importStat(type, last.run) : exportStat(type, last.run);
}

export function lastRunLabel(last: LastRun | null): string {
  const started = parseDate(last?.run?.startedAt);
  return started ? `Last run ${started.toLocaleString()}` : '';
}
