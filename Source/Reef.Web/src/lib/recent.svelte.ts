export interface RecentProfile {
  kind: 'export' | 'import';
  id: string;
  name: string;
  status?: 'ok' | 'err' | 'idle';
}

function load(): RecentProfile[] {
  try {
    return (JSON.parse(localStorage.getItem('reef_recent') ?? '[]') as RecentProfile[]).filter((r) => typeof r.id === 'string');
  } catch {
    return [];
  }
}

export const recent = $state({ items: load() });

export function remember(item: RecentProfile): void {
  recent.items = [item, ...recent.items.filter((r) => !(r.kind === item.kind && r.id === item.id))].slice(0, 5);
  try {
    localStorage.setItem('reef_recent', JSON.stringify(recent.items));
  } catch {}
}

export function forget(kind: string, id: string): void {
  recent.items = recent.items.filter((r) => !(r.kind === kind && r.id === id));
  try {
    localStorage.setItem('reef_recent', JSON.stringify(recent.items));
  } catch {}
}
