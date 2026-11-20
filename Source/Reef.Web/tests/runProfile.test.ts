import { afterEach, describe, expect, it, vi } from 'vitest';
import { api } from '../src/lib/api';
import { pending, settle } from '../src/lib/confirm.svelte';
import { runProfile } from '../src/lib/profiles';

describe('runProfile', () => {
  afterEach(() => {
    settle(false);
    vi.restoreAllMocks();
  });

  it('sends no request for an email run the user declines', async () => {
    const post = vi.spyOn(api, 'post').mockResolvedValue({ status: 'Success' });
    const run = runProfile('export', '01ARZ3NDEKTSV4RRFFQ69G5FAV', 'Invoices', { sendsEmail: true });
    await vi.waitFor(() => expect(pending.request).not.toBeNull());
    settle(false);

    expect(await run).toBeNull();
    expect(post).not.toHaveBeenCalled();
  });

  it('runs an email profile after confirmation', async () => {
    const post = vi.spyOn(api, 'post').mockResolvedValue({ status: 'Success' });
    const run = runProfile('export', '01ARZ3NDEKTSV4RRFFQ69G5FAV', 'Invoices', { sendsEmail: true });
    await vi.waitFor(() => expect(pending.request).not.toBeNull());
    settle(true);

    expect(await run).toEqual({ status: 'Success' });
    expect(post).toHaveBeenCalledOnce();
  });

  it('runs a file export without asking', async () => {
    const post = vi.spyOn(api, 'post').mockResolvedValue({ status: 'Success' });

    await runProfile('export', '01ARZ3NDEKTSV4RRFFQ69G5FAV', 'Orders');

    expect(pending.request).toBeNull();
    expect(post).toHaveBeenCalledOnce();
  });
});
