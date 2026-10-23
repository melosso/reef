import { navigate } from './router.svelte';
import { clearSession, token } from './session.svelte';

export interface FieldError {
  nodeId?: string;
  field?: string;
  message: string;
}

export class ApiError extends Error {
  status: number;
  errors: FieldError[];
  body: unknown;

  constructor(message: string, status: number, errors: FieldError[], body: unknown) {
    super(message);
    this.status = status;
    this.errors = errors;
    this.body = body;
  }
}

type Method = 'GET' | 'POST' | 'PUT' | 'DELETE';

function messageOf(body: any, status: number): string {
  if (body && typeof body === 'object') {
    const text = body.error ?? body.message ?? body.detail ?? body.title;
    if (typeof text === 'string' && text) return text;
  }
  if (typeof body === 'string' && body && body.length < 300) return body;
  return `Request failed with status ${status}`;
}

function errorsOf(body: any): FieldError[] {
  if (!body || typeof body !== 'object') return [];
  if (Array.isArray(body.errors)) {
    return body.errors.map((e: any) => (typeof e === 'string' ? { message: e } : e));
  }
  if (body.errors && typeof body.errors === 'object') {
    return Object.entries(body.errors).flatMap(([field, messages]) =>
      ([] as string[]).concat(messages as string[]).map((message) => ({ field, message })),
    );
  }
  return [];
}

export async function request<T = any>(method: Method, url: string, body?: unknown, init: RequestInit = {}): Promise<T> {
  const headers: Record<string, string> = { Accept: 'application/json' };
  const auth = token();
  if (auth) headers.Authorization = `Bearer ${auth}`;
  if (body !== undefined && !(body instanceof FormData)) headers['Content-Type'] = 'application/json';

  const response = await fetch(url, {
    method,
    headers,
    credentials: 'same-origin',
    body: body === undefined ? undefined : body instanceof FormData ? body : JSON.stringify(body),
    ...init,
  });

  const type = response.headers.get('content-type') ?? '';
  const parsed =
    response.status === 204
      ? null
      : type.includes('json')
        ? await response.json().catch(() => null)
        : await response.text().catch(() => null);

  if (response.status === 401 && !url.startsWith('/api/auth/')) {
    clearSession();
    navigate('/logoff');
  }
  if (!response.ok) throw new ApiError(messageOf(parsed, response.status), response.status, errorsOf(parsed), parsed);
  return parsed as T;
}

export const api = {
  get: <T = any>(url: string) => request<T>('GET', url),
  post: <T = any>(url: string, body: unknown = {}) => request<T>('POST', url, body),
  put: <T = any>(url: string, body: unknown = {}) => request<T>('PUT', url, body),
  del: <T = any>(url: string) => request<T>('DELETE', url),
};

export async function download(url: string, fallbackName: string): Promise<void> {
  const auth = token();
  const response = await fetch(url, { headers: auth ? { Authorization: `Bearer ${auth}` } : {} });
  if (!response.ok) throw new ApiError(`Download failed with status ${response.status}`, response.status, [], null);
  const disposition = response.headers.get('content-disposition') ?? '';
  const name = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition)?.[1] ?? fallbackName;
  const href = URL.createObjectURL(await response.blob());
  const a = Object.assign(document.createElement('a'), { href, download: decodeURIComponent(name) });
  a.click();
  URL.revokeObjectURL(href);
}
