export const route = $state({ path: location.pathname, query: new URLSearchParams(location.search) });

type Guard = () => boolean;
let leaveGuard: Guard | null = null;

export function setLeaveGuard(guard: Guard | null): void {
  leaveGuard = guard;
}

function sync(): void {
  route.path = location.pathname;
  route.query = new URLSearchParams(location.search);
}

export function navigate(to: string, replace = false): void {
  if (to === location.pathname + location.search) return;
  if (leaveGuard && !leaveGuard()) return;
  leaveGuard = null;
  if (replace) history.replaceState(null, '', to);
  else history.pushState(null, '', to);
  sync();
}

export function setQuery(key: string, value: string | null): void {
  const url = new URL(location.href);
  if (value) url.searchParams.set(key, value);
  else url.searchParams.delete(key);
  history.replaceState(null, '', url.pathname + url.search);
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

window.addEventListener('popstate', sync);

document.addEventListener('click', (e) => {
  if (e.defaultPrevented || e.button !== 0 || e.metaKey || e.ctrlKey || e.shiftKey || e.altKey) return;
  const a = (e.target as Element).closest('a');
  if (!a || a.target || a.hasAttribute('download') || a.origin !== location.origin) return;
  if (a.pathname.startsWith('/api/') || a.getAttribute('href')?.startsWith('#')) return;
  e.preventDefault();
  navigate(a.pathname + a.search);
});
