<script lang="ts">
  import { Pencil, Plus, Trash2 } from '@lucide/svelte';
  import Page from '../components/Page.svelte';
  import Sheet from '../components/Sheet.svelte';
  import StatusMark from '../components/StatusMark.svelte';
  import { api } from '../lib/api';
  import { confirm } from '../lib/confirm.svelte';
  import { formatDateTime, formatRelative } from '../lib/format';
  import { notify } from '../lib/notify';
  import { route, setQuery } from '../lib/router.svelte';
  import { tooltip } from '../lib/tooltip';

  const TYPES: Record<string, string> = { SqlServer: 'SQL Server', MySQL: 'MySQL', PostgreSQL: 'PostgreSQL', Sqlite: 'SQLite' };
  const EXAMPLES: Record<string, string> = {
    SqlServer: 'Server=localhost;Database=mydb;User Id=sa;Password=***;TrustServerCertificate=True;',
    MySQL: 'Server=localhost;Port=3306;Database=mydb;User=root;Password=***;',
    PostgreSQL: 'Host=localhost;Database=mydb;Username=postgres;Password=***;',
    Sqlite: 'Data Source=/path/to/staging.db',
  };

  interface Form {
    id?: number;
    name: string;
    type: string;
    connectionString: string;
    tags: string;
    isActive: boolean;
  }

  let connections = $state<any[]>([]);
  let loaded = $state(false);
  let search = $state('');
  let tag = $state('');
  let form = $state<Form | null>(null);
  let busy = $state<string | null>(null);

  async function load() {
    connections = await api.get<any[]>('/api/connections').catch((e) => {
      notify.fail(e, 'connections');
      return [];
    });
    loaded = true;
  }

  $effect(() => {
    load();
  });

  $effect(() => {
    const id = route.query.get('id');
    if (id && loaded && form?.id !== Number(id)) edit(Number(id));
  });

  const tags = $derived(
    [
      ...new Set(
        connections.flatMap((c) =>
          (c.tags ?? '')
            .split(',')
            .map((t: string) => t.trim())
            .filter(Boolean),
        ),
      ),
    ].sort(),
  );
  const visible = $derived(
    connections.filter((c) => {
      const term = search.trim().toLowerCase();
      const cTags = (c.tags ?? '').split(',').map((t: string) => t.trim());
      return (
        (!term ||
          [c.name, TYPES[c.type] ?? c.type, c.tags].some((v) =>
            String(v ?? '')
              .toLowerCase()
              .includes(term),
          )) &&
        (!tag || cTags.includes(tag))
      );
    }),
  );

  function create() {
    form = { name: '', type: 'SqlServer', connectionString: '', tags: '', isActive: true };
  }

  async function edit(id: number) {
    try {
      const c = await api.get(`/api/connections/${id}`);
      form = { id: c.id, name: c.name, type: c.type, connectionString: c.connectionString ?? '', tags: c.tags ?? '', isActive: c.isActive };
    } catch (e) {
      notify.fail(e);
    }
  }

  function close() {
    form = null;
    if (route.query.get('id')) setQuery('id', null);
  }

  async function test(f: Form, quiet = false) {
    busy = 'test';
    try {
      const result = await api.post('/api/connections/test', {
        type: f.type,
        connectionString: f.connectionString,
        ...(f.id ? { connectionId: f.id } : {}),
      });
      if (result?.success)
        notify.success(`${f.name || 'Connection'} reachable in ${result.responseTimeMs} ms`, { key: `conn-test-${f.id ?? 'new'}` });
      else
        notify[quiet ? 'warning' : 'error'](`Connection test failed. ${result?.message ?? ''}`.trim(), {
          key: `conn-test-${f.id ?? 'new'}`,
        });
      await load();
    } catch (e) {
      notify.fail(e, `conn-test-${f.id ?? 'new'}`);
    } finally {
      busy = null;
    }
  }

  async function save(e: SubmitEvent) {
    e.preventDefault();
    if (!form) return;
    busy = 'save';
    try {
      const body = {
        name: form.name,
        type: form.type,
        connectionString: form.connectionString,
        tags: form.tags,
        isActive: form.isActive,
        hash: '',
      };
      const saved = form.id ? await api.put(`/api/connections/${form.id}`, body) : await api.post('/api/connections', body);
      notify.success(`${form.name} saved`, { key: 'conn-save' });
      const created = !form.id;
      const snapshot = { ...form, id: saved?.id ?? form.id };
      close();
      await load();
      if (created) test(snapshot, true);
    } catch (err) {
      notify.fail(err, 'conn-save');
    } finally {
      busy = null;
    }
  }

  async function toggle() {
    if (!form?.id) return;
    const off = form.isActive;
    if (
      !(await confirm({
        title: `${off ? 'Turn off' : 'Turn on'} ${form.name}?`,
        message: off
          ? 'Profiles, jobs and webhooks that use this connection fail until the connection is turned on.'
          : 'The connection becomes available to profiles and jobs.',
        confirm: off ? 'Turn off' : 'Turn on',
        danger: off,
      }))
    )
      return;
    try {
      const current = await api.get(`/api/connections/${form.id}`);
      await api.put(`/api/connections/${form.id}`, { ...current, isActive: !off });
      notify.success(`${form.name} turned ${off ? 'off' : 'on'}`);
      close();
      await load();
    } catch (e) {
      notify.fail(e);
    }
  }

  async function remove(c: { id: number; name: string }) {
    if (
      !(await confirm({
        title: `Delete ${c.name}?`,
        message: 'Deleted connections cannot be restored.',
        confirm: 'Delete connection',
        danger: true,
      }))
    )
      return;
    try {
      await api.del(`/api/connections/${c.id}`);
      notify.success(`${c.name} deleted`);
      if (form?.id === c.id) close();
      await load();
    } catch (e) {
      notify.fail(e);
    }
  }
</script>

<Page crumbs={[{ label: 'Connections' }]}>
  {#snippet actions()}
    <button type="button" class="btn primary" onclick={create}><Plus size={15} />New connection</button>
  {/snippet}
  <div class="page-body">
    <div class="toolbar">
      <input type="search" placeholder="Search name, type or tag" aria-label="Search connections" bind:value={search} />
      <select aria-label="Tag" bind:value={tag}>
        <option value="">All tags</option>
        {#each tags as t}<option value={t}>{t}</option>{/each}
      </select>
      <span class="spacer"></span>
      <span class="muted"><span class="num">{visible.length}</span> of <span class="num">{connections.length}</span></span>
    </div>
    <div class="table-wrap">
      <table class="data">
        <thead
          ><tr><th>Name</th><th>Type</th><th>Tags</th><th>Last test</th><th>Status</th><th><span class="sr">Actions</span></th></tr></thead
        >
        <tbody>
          {#each visible as c (c.id)}
            {@const ok = c.lastTestResult?.startsWith('Success')}
            <tr
              class="clickable"
              onclick={(e) => {
                if (!(e.target as Element).closest('button')) setQuery('id', String(c.id));
              }}
            >
              <td class="strong">{c.name}</td>
              <td>{TYPES[c.type] ?? c.type}</td>
              <td>
                {#each (c.tags ?? '')
                  .split(',')
                  .map((t: string) => t.trim())
                  .filter(Boolean) as t}
                  <button type="button" class="link tag" onclick={() => (tag = t)}>{t}</button>
                {/each}
              </td>
              <td>
                {#if c.lastTestedAt}
                  <span use:tooltip={c.lastTestResult ?? formatDateTime(c.lastTestedAt)}
                    ><StatusMark kind={ok ? 'ok' : 'err'} label={`${ok ? 'Passed' : 'Failed'} ${formatRelative(c.lastTestedAt)}`} /></span
                  >
                {:else}<span class="muted">Never tested</span>{/if}
              </td>
              <td><StatusMark kind={c.isActive ? 'ok' : 'off'} label={c.isActive ? 'On' : 'Off'} /></td>
              <td class="actions">
                <button
                  type="button"
                  class="btn ghost icon"
                  aria-label="Edit {c.name}"
                  use:tooltip={'Edit'}
                  onclick={() => setQuery('id', String(c.id))}><Pencil size={15} /></button
                >
                <button
                  type="button"
                  class="btn ghost icon danger"
                  aria-label="Delete {c.name}"
                  use:tooltip={'Delete'}
                  onclick={() => remove(c)}><Trash2 size={15} /></button
                >
              </td>
            </tr>
          {:else}
            <tr
              ><td colspan="6" class="empty">
                {#if !loaded}Loading connections
                {:else if connections.length === 0}
                  <div class="empty-state">
                    <h2>No connections</h2>
                    <p>A connection stores the database address and credentials that profiles query.</p>
                    <button type="button" class="btn primary" onclick={create}>New connection</button>
                  </div>
                {:else}No connection matches the search.{/if}
              </td></tr
            >
          {/each}
        </tbody>
      </table>
    </div>
  </div>
</Page>

{#if form}
  <Sheet title={form.id ? form.name || 'Connection' : 'New connection'} onclose={close}>
    <form id="connection-form" class="fields" onsubmit={save}>
      <div class="field"><label for="c-name">Name</label><input id="c-name" bind:value={form.name} autocomplete="off" /></div>
      <div class="field">
        <label for="c-type">Type</label>
        <select id="c-type" bind:value={form.type}
          >{#each Object.entries(TYPES) as [value, label]}<option {value}>{label}</option>{/each}</select
        >
      </div>
      <div class="field">
        <label for="c-string"
          >Connection string
          {#if !form.id}<button type="button" class="link small" onclick={() => (form!.connectionString = EXAMPLES[form!.type])}
              >Insert example</button
            >{/if}
        </label>
        <textarea id="c-string" rows="4" spellcheck="false" autocomplete="off" bind:value={form.connectionString}></textarea>
      </div>
      <div class="field">
        <label for="c-tags">Tags</label><input
          id="c-tags"
          placeholder="production, erp"
          bind:value={form.tags}
          use:tooltip={'Comma-separated'}
        />
      </div>
    </form>
    {#snippet footer()}
      {#if form?.id}
        <button type="button" class="btn danger" onclick={() => remove({ id: form!.id!, name: form!.name })}>Delete</button>
        <button type="button" class="btn" onclick={toggle}>{form.isActive ? 'Turn off' : 'Turn on'}</button>
      {/if}
      <span class="grow"></span>
      <button type="button" class="btn" aria-busy={busy === 'test'} onclick={() => test(form!)}>Test connection</button>
      <button type="submit" form="connection-form" class="btn primary" aria-busy={busy === 'save'}>Save</button>
    {/snippet}
  </Sheet>
{/if}

<style>
  .strong {
    font-weight: 500;
  }
  .tag {
    margin-right: 8px;
    font-size: 12px;
    text-decoration: none;
  }
  .small {
    font-size: 12px;
    font-weight: 400;
    margin-left: auto;
  }
  label {
    display: flex;
    align-items: center;
    gap: 6px;
  }
  .grow {
    flex: 1;
  }
</style>
