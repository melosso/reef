const DETAIL_COLLECTIONS = ['/connections', '/destinations', '/templates', '/jobs'];
const ADMIN_TABS: Record<string, string> = { apikeys: 'api-keys' };
const ACCOUNT_TABS: Record<string, string> = { mfa: 'security' };

export const PUBLIC_PATHS = ['/login', '/logout'];

// Maps legacy and non-canonical URLs to their canonical form. Returns null for canonical URLs.
export function canonicalUrl(path: string, search: string): string | null {
  const query = new URLSearchParams(search);
  const take = (key: string) => {
    const value = query.get(key);
    query.delete(key);
    return value;
  };
  let next = path.replace(/\/+$/, '').replace(/^\/[^/]*/, (first) => first.toLowerCase()) || '/';

  if (next === '/' || next === '/index') next = '/login';
  else if (next === '/logoff') next = '/logout';
  else if (next === '/email-approvals') {
    const tab = take('tab');
    next = tab ? `/approvals/${tab}` : '/approvals';
  } else if (next === '/admin' || next === '/account') {
    const tab = take('tab');
    const renamed = next === '/admin' ? ADMIN_TABS : ACCOUNT_TABS;
    if (tab) next = `${next}/${renamed[tab] ?? tab}`;
  } else if (DETAIL_COLLECTIONS.includes(next)) {
    const id = take('id');
    if (id) next = `${next}/${id}`;
  } else if (next === '/executions') {
    const id = take('id');
    if (id) next = `/executions/${id}`;
  } else if (next === '/profiles' || next.startsWith('/profiles/')) {
    next = '/workflows';
  }

  const rest = query.toString();
  const url = rest ? `${next}?${rest}` : next;
  return url === path + search ? null : url;
}
