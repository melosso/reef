import { toast } from 'svelte-sonner';

const KEY = 'api-offline';
let probe: ReturnType<typeof setInterval> | null = null;

export function markOffline(): void {
  if (probe) return;
  toast.error('Reef API is unreachable', { id: KEY, description: 'Reconnecting in the background.', duration: Number.POSITIVE_INFINITY });
  probe = setInterval(async () => {
    const response = await fetch('/health', { cache: 'no-store' }).catch(() => null);
    if (response?.ok) markOnline();
  }, 5000);
}

export function markOnline(): void {
  if (!probe) return;
  clearInterval(probe);
  probe = null;
  toast.success('Reef API is back online', { id: KEY, duration: 3000 });
}
