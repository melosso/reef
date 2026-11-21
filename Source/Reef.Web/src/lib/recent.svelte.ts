export interface RecentWorkflow {
  id: string;
  name: string;
  status?: 'ok' | 'err' | 'idle';
}

const KEY = 'reef_recent_workflows';

function load(): RecentWorkflow[] {
  try {
    return (JSON.parse(localStorage.getItem(KEY) ?? '[]') as RecentWorkflow[]).filter((r) => typeof r.id === 'string');
  } catch {
    return [];
  }
}

export const recent = $state({ items: load() });

function store() {
  try {
    localStorage.setItem(KEY, JSON.stringify(recent.items));
  } catch {}
}

export function remember(item: RecentWorkflow): void {
  recent.items = [item, ...recent.items.filter((r) => r.id !== item.id)].slice(0, 5);
  store();
}

export function forget(id: string): void {
  recent.items = recent.items.filter((r) => r.id !== id);
  store();
}
