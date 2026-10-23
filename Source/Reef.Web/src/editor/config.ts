import type { CatalogField } from './types';

export function getPath(obj: Record<string, any>, path: string): any {
  return path.split('.').reduce<any>((current, key) => (current && typeof current === 'object' ? current[key] : undefined), obj);
}

export function setPath(obj: Record<string, any>, path: string, value: unknown): void {
  const keys = path.split('.');
  let current = obj;
  for (const key of keys.slice(0, -1)) {
    if (!current[key] || typeof current[key] !== 'object' || Array.isArray(current[key])) current[key] = {};
    current = current[key];
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

export function visible(field: CatalogField, config: Record<string, any>): boolean {
  if (field.hidden) return false;
  if (!field.showWhen) return true;
  const value = getPath(config, field.showWhen.key);
  if (!field.showWhen.values) return value !== undefined && value !== null && value !== '' && value !== false;
  return field.showWhen.values.some((v) => v.toLowerCase() === textOf(value).toLowerCase());
}

export function defaultsFor(fields: CatalogField[]): Record<string, any> {
  const config: Record<string, any> = {};
  for (const field of fields) {
    if (field.default !== undefined && field.default !== null) setPath(config, field.key, structuredClone(field.default));
  }
  return config;
}
