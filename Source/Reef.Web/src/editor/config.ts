import type { CatalogField, Config } from './types';

export function getPath(obj: Config, path: string): unknown {
  return path
    .split('.')
    .reduce<unknown>((current, key) => (current && typeof current === 'object' ? (current as Config)[key] : undefined), obj);
}

export function setPath(obj: Config, path: string, value: unknown): void {
  const keys = path.split('.');
  let current = obj;
  for (const key of keys.slice(0, -1)) {
    if (!current[key] || typeof current[key] !== 'object' || Array.isArray(current[key])) current[key] = {};
    current = current[key] as Config;
  }
  const last = keys[keys.length - 1];
  if (value === undefined) delete current[last];
  else current[last] = value;
}

export function textOf(value: unknown): string {
  if (value === undefined || value === null) return '';
  if (typeof value === 'boolean') return value ? 'true' : 'false';
  return String(value);
}

export function visible(field: CatalogField, config: Config): boolean {
  if (field.hidden) return false;
  if (!field.showWhen) return true;
  const value = getPath(config, field.showWhen.key);
  if (!field.showWhen.values) return value !== undefined && value !== null && value !== '' && value !== false;
  return field.showWhen.values.some((v) => v.toLowerCase() === textOf(value).toLowerCase());
}

export function defaultsFor(fields: CatalogField[]): Config {
  const config: Config = {};
  for (const field of fields) {
    if (field.default !== undefined && field.default !== null) setPath(config, field.key, structuredClone(field.default));
  }
  return config;
}
