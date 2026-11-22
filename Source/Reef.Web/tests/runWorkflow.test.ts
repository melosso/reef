import { afterEach, describe, expect, it, vi } from 'vitest';
import { api } from '../src/lib/api';
import { pending, settle } from '../src/lib/confirm.svelte';
import { runWorkflow } from '../src/lib/workflows';
import type { Workflow } from '../src/models/Workflow';

const workflow = (config: Record<string, unknown> | null): Workflow => ({
  id: '01ARZ3NDEKTSV4RRFFQ69G5FAV',
  name: 'Invoices',
  active: false,
  maxPasses: 1000,
  nodes: [{ id: 'start', type: 'manual', x: 0, y: 0, config: {} }, ...(config ? [{ id: 'mail', type: 'email', x: 0, y: 0, config }] : [])],
  edges: [],
});

describe('runWorkflow', () => {
  afterEach(() => {
    settle(false);
    vi.restoreAllMocks();
  });

  it('sends no request for an email run the user declines', async () => {
    const post = vi.spyOn(api, 'post').mockResolvedValue({ status: 'Success' });
    const run = runWorkflow(workflow({}));
    await vi.waitFor(() => expect(pending.request).not.toBeNull());
    settle(false);

    expect(await run).toBeNull();
    expect(post).not.toHaveBeenCalled();
  });

  it('runs an email workflow after confirmation', async () => {
    const post = vi.spyOn(api, 'post').mockResolvedValue({ status: 'Success' });
    const run = runWorkflow(workflow({}));
    await vi.waitFor(() => expect(pending.request).not.toBeNull());
    settle(true);

    expect(await run).toEqual({ status: 'Success' });
    expect(post).toHaveBeenCalledOnce();
  });

  it('runs without asking when emails wait for approval or there is no email step', async () => {
    const post = vi.spyOn(api, 'post').mockResolvedValue({ status: 'Success' });

    await runWorkflow(workflow({ RequireApproval: true }));
    await runWorkflow(workflow(null));

    expect(pending.request).toBeNull();
    expect(post).toHaveBeenCalledTimes(2);
  });
});
