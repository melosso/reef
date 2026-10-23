export interface SessionData {
  token: string;
  username: string;
  role: string;
  displayName?: string | null;
}

function read(key: string): string | null {
  try {
    return localStorage.getItem(key);
  } catch {
    return null;
  }
}

function write(key: string, value: string | null | undefined): void {
  try {
    if (value) localStorage.setItem(key, value);
    else localStorage.removeItem(key);
  } catch {}
}

export const session = $state({
  token: read('reef_token'),
  username: read('reef_username') ?? '',
  role: read('reef_role') ?? '',
  displayName: read('reef_display_name'),
});

export function token(): string | null {
  return session.token;
}

export function isAdmin(): boolean {
  return session.role.toLowerCase() === 'admin';
}

export function displayName(): string {
  return session.displayName || session.username || 'User';
}

export function storeSession(data: SessionData): void {
  session.token = data.token;
  session.username = data.username;
  session.role = data.role;
  session.displayName = data.displayName ?? null;
  write('reef_token', data.token);
  write('reef_username', data.username);
  write('reef_role', data.role);
  write('reef_display_name', data.displayName);
}

export function clearSession(): void {
  session.token = null;
  session.username = '';
  session.role = '';
  session.displayName = null;
  ['reef_token', 'reef_username', 'reef_role', 'reef_display_name'].forEach((k) => write(k, null));
}

export async function restoreFromCookie(): Promise<boolean> {
  const response = await fetch('/api/auth/refresh', { method: 'POST', credentials: 'same-origin' }).catch(() => null);
  if (!response?.ok) return false;
  const data = await response.json().catch(() => null);
  if (!data?.token) return false;
  storeSession(data);
  return true;
}

export async function refresh(): Promise<void> {
  if (!session.token) return;
  const response = await fetch('/api/auth/refresh', { method: 'POST', headers: { Authorization: `Bearer ${session.token}` } }).catch(
    () => null,
  );
  if (!response?.ok) return;
  const data = await response.json().catch(() => null);
  if (data?.token) storeSession(data);
}
