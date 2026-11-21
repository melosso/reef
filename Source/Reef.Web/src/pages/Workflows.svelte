<script lang="ts">
  import { Play, Plus, Trash2 } from '@lucide/svelte';
  import Page from '../components/Page.svelte';
  import StatusMark from '../components/StatusMark.svelte';
  import { api } from '../lib/api';
  import { confirm } from '../lib/confirm.svelte';
  import { formatDateTime, formatRelative } from '../lib/format';
  import { notify } from '../lib/notify';
  import { navigate } from '../lib/router.svelte';
  import { tooltip } from '../lib/tooltip';
  import type { Workflow } from '../models/Workflow';
  import { forget } from '../lib/recent.svelte';
  import { runWorkflow } from '../lib/workflows';

  const TRIGGERS: Record<string, string> = { manual: 'Manual', schedule: 'Schedule', webhook: 'Webhook' };

  let workflows = $state<Workflow[]>([]);
  let loaded = $state(false);
  let search = $state('');
  let busy = $state<string | null>(null);

  async function load() {
    workflows = await api.get<Workflow[]>('/api/workflows').catch((e) => {
      notify.fail(e, 'workflows');
      return [];
    });
    loaded = true;
  }

  $effect(() => {
    load();
  });

  const visible = $derived(workflows.filter((w) => w.name.toLowerCase().includes(search.trim().toLowerCase())));

  function triggers(w: Workflow): string {
    return [...new Set(w.nodes.filter((n) => TRIGGERS[n.type]).map((n) => TRIGGERS[n.type]))].join(', ') || 'None';
  }

  async function run(w: Workflow) {
    busy = w.id!;
    try {
      await runWorkflow(w);
    } catch (e) {
      notify.fail(e, `run-${w.id}`);
    } finally {
      busy = null;
    }
  }

  async function remove(w: Workflow) {
    const ok = await confirm({
      title: `Delete ${w.name}?`,
      message: 'The workflow, its run history and its Smart Sync state are deleted.',
      confirm: 'Delete',
      danger: true,
    });
    if (!ok) return;
    try {
      await api.del(`/api/workflows/${w.id}`);
      forget(w.id!);
      notify.success(`${w.name} deleted`);
      await load();
    } catch (e) {
      notify.fail(e, `delete-${w.id}`);
    }
  }
</script>

<Page crumbs={[{ label: 'Workflows' }]}>
  {#snippet actions()}
    <button type="button" class="btn primary" onclick={() => navigate('/workflows/new')}><Plus size={15} />New workflow</button>
  {/snippet}
  <div class="page-body">
    <div class="toolbar">
      <input type="search" placeholder="Search name" aria-label="Search workflows" bind:value={search} />
      <span class="spacer"></span>
      <span class="muted"><span class="num">{visible.length}</span> of <span class="num">{workflows.length}</span></span>
    </div>
    <div class="table-wrap">
      <table class="data">
        <thead>
          <tr><th>Name</th><th>Triggers</th><th>Steps</th><th>Updated</th><th>Status</th><th><span class="sr">Actions</span></th></tr>
        </thead>
        <tbody>
          {#each visible as w (w.id)}
            <tr
              class="clickable"
              onclick={(e) => {
                if (!(e.target as Element).closest('button')) navigate(`/workflows/${w.id}`);
              }}
            >
              <td class="strong">{w.name}</td>
              <td>{triggers(w)}</td>
              <td class="num">{w.nodes.length}</td>
              <td><span use:tooltip={formatDateTime(w.updatedAt)}>{formatRelative(w.updatedAt)}</span></td>
              <td>
                <span use:tooltip={'Schedule and webhook triggers start the workflow only when it is on'}>
                  <StatusMark kind={w.active ? 'ok' : 'off'} label={w.active ? 'On' : 'Off'} />
                </span>
              </td>
              <td class="actions">
                <button
                  type="button"
                  class="btn ghost icon"
                  aria-label="Run {w.name}"
                  aria-busy={busy === w.id}
                  disabled={busy === w.id}
                  use:tooltip={'Run from the manual trigger'}
                  onclick={() => run(w)}
                >
                  <Play size={15} />
                </button>
                <button
                  type="button"
                  class="btn ghost icon danger"
                  aria-label="Delete {w.name}"
                  use:tooltip={'Delete'}
                  onclick={() => remove(w)}
                >
                  <Trash2 size={15} />
                </button>
              </td>
            </tr>
          {:else}
            <tr>
              <td colspan="6" class="empty">
                {#if !loaded}Loading workflows
                {:else if workflows.length === 0}
                  <div class="empty-state">
                    <h2>No workflows</h2>
                    <p>A workflow connects triggers, queries, transforms and deliveries on one canvas.</p>
                    <button type="button" class="btn primary" onclick={() => navigate('/workflows/new')}>New workflow</button>
                  </div>
                {:else}No workflow matches the search.{/if}
              </td>
            </tr>
          {/each}
        </tbody>
      </table>
    </div>
  </div>
</Page>
