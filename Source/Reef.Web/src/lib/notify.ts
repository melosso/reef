import { toast } from 'svelte-sonner';
import { ApiError } from './api';

interface Options {
  key?: string;
  description?: string;
}

export const notify = {
  success: (message: string, o: Options = {}) => toast.success(message, { id: o.key, description: o.description, duration: 4000 }),
  info: (message: string, o: Options = {}) => toast.info(message, { id: o.key, description: o.description, duration: 5000 }),
  warning: (message: string, o: Options = {}) => toast.warning(message, { id: o.key, description: o.description, duration: 8000 }),
  error: (message: string, o: Options = {}) =>
    toast.error(message, { id: o.key, description: o.description, duration: 12000, closeButton: true }),
  fail: (error: unknown, key?: string) => {
    if (error instanceof ApiError && error.status === 0) return;
    const message = error instanceof Error ? error.message : String(error);
    const extra =
      error instanceof ApiError && error.errors.length > 1 ? `${error.errors.length - 1} more problems in this form` : undefined;
    return notify.error(message, { key, description: extra });
  },
};

export async function copyText(text: string): Promise<void> {
  try {
    await navigator.clipboard.writeText(text);
    toast.success('Copied to clipboard', { id: 'copied', duration: 1500 });
  } catch {
    toast.error('Copy failed. Clipboard access is blocked.', { id: 'copied', duration: 3000 });
  }
}

document.addEventListener('click', (e) => {
  const target = e.target as Element;
  if (target.closest('button')) return;
  const item = target.closest('[data-sonner-toast]');
  if (!item || item.getAttribute('data-id') === 'copied') return;
  const text = [item.querySelector('[data-title]')?.textContent, item.querySelector('[data-description]')?.textContent]
    .filter(Boolean)
    .join('\n');
  if (text) copyText(text);
});
