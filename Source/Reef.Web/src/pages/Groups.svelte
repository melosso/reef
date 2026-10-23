<script lang="ts">
  import { Folder, Pencil, Plus, Trash2 } from '@lucide/svelte';
  import Page from '../components/Page.svelte';
  import Sheet from '../components/Sheet.svelte';
  import { api } from '../lib/api';
  import { confirm } from '../lib/confirm.svelte';
  import { notify } from '../lib/notify';
  import { tooltip } from '../lib/tooltip';

  let groups = $state<any[]>([]);
  let counts = $state<Record<number, number>>({});
  let loaded = $state(false);
  let form = $state<{ id?: number; name: string; description: string } | null>(null);
  let busy = $state(false);

  async function load() {
    const [g, exports, imports] = await Promise.all([
      api.get<any[]>('/api/groups').catch((e) => {
        notify.fail(e, 'groups');
        return [];
      }),
      api.get<any[]>('/api/profiles').catch(() => []),
      api.get<any[]>('/api/profiles?type=import').catch(() => []),
    ]);
    groups = g ?? [];
    const next: Record<number, number> = {};
    [...(exports ?? []), ...(imports ?? [])].forEach((p) => {
      if (p.groupId) next[p.groupId] = (next[p.groupId] ?? 0) + 1;
    });
    counts = next;
    loaded = true;
  }

  $effect(() => {
    load();
  });

  async function save(e: SubmitEvent) {
    e.preventDefault();
    if (!form) return;
    busy = true;
    try {
      const body = { name: form.name, description: form.description };
      if (form.id) await api.put(`/api/groups/${form.id}`, body);
      else await api.post('/api/groups', body);
      notify.success(`${form.name} saved`, { key: 'group-save' });
      form = null;
      await load();
    } catch (err) {
      notify.fail(err, 'group-save');
    } finally {
      busy = false;
    }
  }

  async function remove(g: any) {
    if (
      !(await confirm({
        title: `Delete ${g.name}?`,
        message: 'Profiles in this group stay and become ungrouped.',
        confirm: 'Delete group',
        danger: true,
      }))
    )
      return;
    try {
      await api.del(`/api/groups/${g.id}`);
      notify.success(`${g.name} deleted`);
      await load();
    } catch (e) {
      notify.fail(e);
    }
  }
</script>

<Page crumbs={[{ label: 'Groups' }]}>
  {#snippet actions()}
    <button type="button" class="btn primary" onclick={() => (form = { name: '', description: '' })}><Plus size={15} />New group</button>
  {/snippet}
  <div class="page-body">
    <div class="table-wrap">
      <table class="data">
        <thead><tr><th>Name</th><th>Description</th><th>Profiles</th><th><span class="sr">Actions</span></th></tr></thead>
        <tbody>
          {#each groups as g (g.id)}
            <tr>
              <td><span class="name"><Folder size={14} />{g.name}</span></td>
              <td class="muted">{g.description ?? ''}</td>
              <td class="num">{counts[g.id] ?? 0}</td>
              <td class="actions">
                <button
                  type="button"
                  class="btn ghost icon"
                  aria-label="Edit {g.name}"
                  use:tooltip={'Edit'}
                  onclick={() => (form = { id: g.id, name: g.name, description: g.description ?? '' })}><Pencil size={15} /></button
                >
                <button
                  type="button"
                  class="btn ghost icon danger"
                  aria-label="Delete {g.name}"
                  use:tooltip={'Delete'}
                  onclick={() => remove(g)}><Trash2 size={15} /></button
                >
              </td>
            </tr>
          {:else}
            <tr
              ><td colspan="4" class="empty">
                {#if !loaded}Loading groups{:else}
                  <div class="empty-state">
                    <h2>No groups</h2>
                    <p>A group collects profiles. Jobs run every profile in a group.</p>
                    <button type="button" class="btn primary" onclick={() => (form = { name: '', description: '' })}>New group</button>
                  </div>
                {/if}
              </td></tr
            >
          {/each}
        </tbody>
      </table>
    </div>
  </div>
</Page>

{#if form}
  <Sheet title={form.id ? 'Edit group' : 'New group'} onclose={() => (form = null)}>
    <form id="group-form" class="fields" onsubmit={save}>
      <div class="field"><label for="g-name">Name</label><input id="g-name" bind:value={form.name} autocomplete="off" /></div>
      <div class="field">
        <label for="g-desc">Description</label><textarea id="g-desc" rows="3" bind:value={form.description}></textarea>
      </div>
    </form>
    {#snippet footer()}
      <button type="button" class="btn" onclick={() => (form = null)}>Cancel</button>
      <button type="submit" form="group-form" class="btn primary" aria-busy={busy}>Save</button>
    {/snippet}
  </Sheet>
{/if}

<style>
  .name {
    display: inline-flex;
    align-items: center;
    gap: 8px;
    font-weight: 500;
  }
  .name :global(svg) {
    color: var(--muted);
  }
</style>
