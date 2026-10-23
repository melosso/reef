import { afterEach, describe, expect, it, vi } from 'vitest';

const toast = vi.hoisted(() => ({ error: vi.fn(), success: vi.fn() }));
vi.mock('svelte-sonner', () => ({ toast }));

const { api, ApiError } = await import('../src/lib/api');

describe('api when the backend is offline', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    toast.error.mockClear();
    toast.success.mockClear();
  });

  it('throws an unreachable error and shows one offline toast', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => Promise.reject(new TypeError('Failed to fetch'))),
    );
    await expect(api.get('/api/jobs')).rejects.toMatchObject({ status: 0 });
    await expect(api.get('/api/jobs')).rejects.toBeInstanceOf(ApiError);
    expect(toast.error).toHaveBeenCalledTimes(1);
    expect(toast.error.mock.calls[0][0]).toBe('Reef API is unreachable');
  });

  it('replaces the offline toast once a request succeeds', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => new Response('[]', { headers: { 'content-type': 'application/json' } })),
    );
    await expect(api.get('/api/jobs')).resolves.toEqual([]);
    expect(toast.success).toHaveBeenCalledWith('Reef API is back online', expect.objectContaining({ id: 'api-offline' }));
  });
});
