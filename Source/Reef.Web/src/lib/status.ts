import type { StatusKind } from '../components/StatusMark.svelte';

export function statusKind(status: string | null | undefined): StatusKind {
  const s = (status ?? '').toLowerCase();
  if (s.includes('partial') || s.includes('skip') || s.includes('cancel')) return 'warn';
  if (s.includes('fail') || s.includes('error') || s.includes('reject')) return 'err';
  if (s.includes('run') || s.includes('pending') || s.includes('queue')) return 'run';
  if (s.includes('success') || s.includes('complete') || s.includes('approved') || s.includes('sent')) return 'ok';
  return 'idle';
}

export function statusLabel(status: string | null | undefined): string {
  if (!status) return 'Unknown';
  if (status === 'PartialSuccess') return 'Partial success';
  return status.replace(/([a-z])([A-Z])/g, '$1 $2').replace(/^./, (c) => c.toUpperCase());
}
