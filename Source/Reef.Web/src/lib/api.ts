import { markOffline, markOnline } from './connectivity';
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

const GATEWAY_ERRORS = new Set([502, 503, 504]);

async function send(url: string, init: RequestInit): Promise<Response> {
  const response = await fetch(url, init).catch((error: unknown) => {
    if (error instanceof DOMException && error.name === 'AbortError') throw error;
    return null;
  });
  if (!response || GATEWAY_ERRORS.has(response.status)) {
    markOffline();
    throw new ApiError('Reef API is unreachable', 0, [], null);
  }
  markOnline();
  return response;
}

interface ErrorBody {
  error?: unknown;
  message?: unknown;
  detail?: unknown;
  title?: unknown;
  errors?: unknown;
}

function messageOf(body: unknown, status: number): string {
  if (body && typeof body === 'object') {
    const { error, message, detail, title } = body as ErrorBody;
    const text = error ?? message ?? detail ?? title;
    if (typeof text === 'string' && text) return text;
  }
  if (typeof body === 'string' && body && body.length < 300) return body;
  return `Request failed with status ${status}`;
}

function errorsOf(body: unknown): FieldError[] {
  if (!body || typeof body !== 'object') return [];
  const { errors } = body as ErrorBody;
  if (Array.isArray(errors)) {
    return errors.map((e: string | FieldError) => (typeof e === 'string' ? { message: e } : e));
  }
  if (errors && typeof errors === 'object') {
    return Object.entries(errors).flatMap(([field, messages]) =>
      ([] as string[]).concat(messages as string[]).map((message) => ({ field, message })),
    );
  }
  return [];
}

export async function request<T = unknown>(method: Method, url: string, body?: unknown, init: RequestInit = {}): Promise<T> {
  const headers: Record<string, string> = { Accept: 'application/json' };
  const auth = token();
  if (auth) headers.Authorization = `Bearer ${auth}`;
  if (body !== undefined && !(body instanceof FormData)) headers['Content-Type'] = 'application/json';

  const response = await send(url, {
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
    navigate('/logout');
  }
  if (!response.ok) throw new ApiError(messageOf(parsed, response.status), response.status, errorsOf(parsed), parsed);
  return parsed as T;
}

export const api = {
  get: <T = unknown>(url: string) => request<T>('GET', url),
  post: <T = unknown>(url: string, body: unknown = {}) => request<T>('POST', url, body),
  put: <T = unknown>(url: string, body: unknown = {}) => request<T>('PUT', url, body),
  del: <T = unknown>(url: string) => request<T>('DELETE', url),
};

export async function download(url: string, fallbackName: string): Promise<void> {
  const auth = token();
  const response = await send(url, { headers: auth ? { Authorization: `Bearer ${auth}` } : {} });
  if (!response.ok) throw new ApiError(`Download failed with status ${response.status}`, response.status, [], null);
  const disposition = response.headers.get('content-disposition') ?? '';
  const name = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition)?.[1] ?? fallbackName;
  const href = URL.createObjectURL(await response.blob());
  const a = Object.assign(document.createElement('a'), { href, download: decodeURIComponent(name) });
  a.click();
  URL.revokeObjectURL(href);
}
