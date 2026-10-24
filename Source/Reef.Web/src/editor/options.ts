import { api } from '../lib/api';
import type { OptionRecord } from '../models/OptionRecord';
import type { Options } from './types';

const SOURCES: Record<
  string,
  { url: string; filter?: (x: OptionRecord) => boolean; map: (x: OptionRecord) => { value: string; label: string } }
> = {
  connections: { url: '/api/connections', map: (c) => ({ value: c.id, label: c.name }) },
  destinations: {
    url: '/api/destinations',
    filter: (d) => d.type !== 'Email',
    map: (d) => ({ value: d.id, label: `${d.name} (${d.type})` }),
  },
  'email-destinations': { url: '/api/destinations', filter: (d) => d.type === 'Email', map: (d) => ({ value: d.id, label: d.name }) },
  templates: { url: '/api/templates', map: (t) => ({ value: t.id, label: t.outputFormat ? `${t.name} (${t.outputFormat})` : t.name }) },
  groups: { url: '/api/groups', map: (g) => ({ value: g.id, label: g.name }) },
};

export async function loadOptions(): Promise<Options> {
  const urls = [...new Set(Object.values(SOURCES).map((s) => s.url))];
  const results = await Promise.all(urls.map((url) => api.get<OptionRecord[]>(url).catch(() => [])));
  const byUrl = Object.fromEntries(urls.map((url, i) => [url, Array.isArray(results[i]) ? results[i] : []]));
  return Object.fromEntries(
    Object.entries(SOURCES).map(([name, source]) => [name, byUrl[source.url].filter(source.filter ?? (() => true)).map(source.map)]),
  );
}
