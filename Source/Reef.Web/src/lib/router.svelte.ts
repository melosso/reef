import { SvelteURLSearchParams } from 'svelte/reactivity';

export const route = $state({ path: location.pathname, query: new SvelteURLSearchParams(location.search) });

type Guard = () => boolean | Promise<boolean>;
let leaveGuard: Guard | null = null;
let current = location.pathname + location.search;

export function setLeaveGuard(guard: Guard | null): void {
  leaveGuard = guard;
}

function sync(): void {
  current = location.pathname + location.search;
  route.path = location.pathname;
  route.query = new SvelteURLSearchParams(location.search);
}

export async function navigate(to: string, replace = false): Promise<void> {
  if (to === location.pathname + location.search) return;
  if (leaveGuard && !(await leaveGuard())) return;
  leaveGuard = null;
  if (replace) history.replaceState(null, '', to);
  else history.pushState(null, '', to);
  sync();
}

export function setQuery(key: string, value: string | null): void {
  const query = new SvelteURLSearchParams(location.search);
  if (value) query.set(key, value);
  else query.delete(key);
  history.replaceState(null, '', query.size ? `${location.pathname}?${query}` : location.pathname);
  sync();
}

export function match(pattern: string, path: string): Record<string, string> | null {
  const a = pattern.split('/').filter(Boolean);
  const b = path.split('/').filter(Boolean);
  if (a.length !== b.length) return null;
  const params: Record<string, string> = {};
  for (let i = 0; i < a.length; i++) {
    if (a[i].startsWith(':')) params[a[i].slice(1)] = decodeURIComponent(b[i]);
    else if (a[i].toLowerCase() !== b[i].toLowerCase()) return null;
  }
  return params;
}

window.addEventListener('popstate', () => {
  if (!leaveGuard) return sync();
  // back and forward already moved the url, so restore it until the guard answers
  const to = location.pathname + location.search;
  history.pushState(null, '', current);
  navigate(to);
});

document.addEventListener('click', (e) => {
  if (e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
  const a = (e.target as Element).closest('a');
  if (!a || a.target || a.hasAttribute('download') || a.origin !== location.origin) return;
  if (a.pathname.startsWith('/api/') || a.getAttribute('href')?.startsWith('#')) return;
  e.preventDefault();
  navigate(a.pathname + a.search);
});
