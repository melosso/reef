<script lang="ts">
  import { Pencil, Plus, Trash2 } from '@lucide/svelte';
  import Page from '../components/Page.svelte';
  import Sheet from '../components/Sheet.svelte';
  import StatusMark from '../components/StatusMark.svelte';
  import { api } from '../lib/api';
  import { confirm } from '../lib/confirm.svelte';
  import {
    DESTINATION_FIELDS,
    DESTINATION_TYPES,
    ENDPOINT_TYPES,
    defaultsFor,
    fromStored,
    maskSecrets,
    toStored,
    type DestField,
  } from '../lib/destinationSchema';
  import { notify } from '../lib/notify';
  import { route, setQuery } from '../lib/router.svelte';
  import { tooltip } from '../lib/tooltip';

  const TYPE_LABEL = Object.fromEntries(DESTINATION_TYPES);

  interface Form {
    id?: number;
    name: string;
    type: string;
    description: string;
    tags: string;
    isActive: boolean;
    config: Record<string, any>;
  }
  interface Endpoint {
    id: number;
    name: string;
    pathSuffix?: string | null;
    method?: string | null;
    uploadFormat?: string | null;
    extraHeaders?: string | null;
    remotePath?: string | null;
    keyPrefix?: string | null;
  }

  let list = $state<any[]>([]);
  let loaded = $state(false);
  let search = $state('');
  let typeFilter = $state('');
  let form = $state<Form | null>(null);
  let advanced = $state(false);
  let busy = $state<string | null>(null);
  let endpoints = $state<Endpoint[]>([]);
  let endpointForm = $state<Endpoint | null>(null);

  async function load() {
    list = await api.get<any[]>('/api/destinations').catch((e) => {
      notify.fail(e, 'destinations');
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

  const visible = $derived(
    list.filter((d) => {
      const term = search.trim().toLowerCase();
      return (
        (!typeFilter || d.type === typeFilter) &&
        (!term ||
          [d.name, d.description, d.tags, TYPE_LABEL[d.type]].some((v) =>
            String(v ?? '')
              .toLowerCase()
              .includes(term),
          ))
      );
    }),
  );

  const fields = $derived(form ? (DESTINATION_FIELDS[form.type] ?? []).filter((f) => !f.when || f.when(form!.config)) : []);
  const hasEndpoints = $derived(!!form?.id && ENDPOINT_TYPES.includes(form.type));

  function create() {
    form = { name: '', type: 'Local', description: '', tags: '', isActive: true, config: defaultsFor('Local') };
    endpoints = [];
    advanced = false;
  }

  function changeType(type: string) {
    if (!form) return;
    form.type = type;
    form.config = defaultsFor(type);
    if (type === 'Sftp') form.config._auth = 'password';
  }

  async function edit(id: number) {
    try {
      const d = await api.get(`/api/destinations/${id}`);
      form = {
        id: d.id,
        name: d.name,
        type: d.type,
        description: d.description ?? '',
        tags: d.tags ?? '',
        isActive: d.isActive,
        config: maskSecrets(d.type, fromStored(d.type, d.configurationJson)),
      };
      advanced = false;
      endpointForm = null;
      endpoints = ENDPOINT_TYPES.includes(d.type) ? await api.get<Endpoint[]>(`/api/destinations/${id}/endpoints`).catch(() => []) : [];
    } catch (e) {
      notify.fail(e);
    }
  }

  function close() {
    form = null;
    endpointForm = null;
    if (route.query.get('id')) setQuery('id', null);
  }

  async function testConfig(configurationJson: string, label: string) {
    busy = 'test';
    try {
      const result = await api.post('/api/destinations/test', { type: form!.type, configurationJson });
      if (result?.success) notify.success(`${label} test passed`, { key: 'dest-test' });
      else notify.error(`${label} test failed. ${result?.message ?? ''}`.trim(), { key: 'dest-test' });
    } catch (e) {
      notify.fail(e, 'dest-test');
    } finally {
      busy = null;
    }
  }

  function testEndpoint(ep: Endpoint) {
    const cfg = JSON.parse(toStored(form!.type, form!.config));
    if (ep.pathSuffix) cfg.Url = `${String(cfg.Url ?? '').replace(/\/+$/, '')}/${ep.pathSuffix.replace(/^\/+/, '')}`;
    if (ep.method) cfg.Method = ep.method;
    return testConfig(JSON.stringify(cfg), ep.name || 'Endpoint');
  }

  async function save(e: SubmitEvent) {
    e.preventDefault();
    if (!form) return;
    busy = 'save';
    try {
      const body = {
        ...(form.id ? { id: form.id } : {}),
        name: form.name,
        description: form.description,
        type: form.type,
        configurationJson: toStored(form.type, form.config),
        tags: form.tags,
        isActive: form.isActive,
      };
      if (form.id) await api.put(`/api/destinations/${form.id}`, body);
      else await api.post('/api/destinations', body);
      notify.success(`${form.name} saved`, { key: 'dest-save' });
      close();
      await load();
    } catch (err) {
      notify.fail(err, 'dest-save');
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
          ? 'Profiles that deliver to this destination fail until the destination is turned on.'
          : 'The destination becomes available to profiles.',
        confirm: off ? 'Turn off' : 'Turn on',
        danger: off,
      }))
    )
      return;
    try {
      const current = await api.get(`/api/destinations/${form.id}`);
      await api.put(`/api/destinations/${form.id}`, { ...current, isActive: !off });
      notify.success(`${form.name} turned ${off ? 'off' : 'on'}`);
      close();
      await load();
    } catch (e) {
      notify.fail(e);
    }
  }

  async function remove(d: { id: number; name: string }) {
    if (
      !(await confirm({
        title: `Delete ${d.name}?`,
        message: 'Deleted destinations cannot be restored.',
        confirm: 'Delete destination',
        danger: true,
      }))
    )
      return;
    try {
      await api.del(`/api/destinations/${d.id}`);
      notify.success(`${d.name} deleted`);
      if (form?.id === d.id) close();
      await load();
    } catch (e) {
      notify.fail(e);
    }
  }

  async function saveEndpoint() {
    if (!form?.id || !endpointForm) return;
    const ep = endpointForm;
    const body = {
      id: ep.id || 0,
      destinationId: form.id,
      name: ep.name.trim(),
      pathSuffix: ep.pathSuffix?.trim() || null,
      method: ep.method || null,
      uploadFormat: ep.uploadFormat || null,
      extraHeaders: ep.extraHeaders?.trim() || null,
      remotePath: ep.remotePath?.trim() || null,
      keyPrefix: ep.keyPrefix?.trim() || null,
      sortOrder: 0,
    };
    try {
      if (ep.id) await api.put(`/api/destinations/${form.id}/endpoints/${ep.id}`, body);
      else await api.post(`/api/destinations/${form.id}/endpoints`, body);
      notify.success(ep.id ? 'Endpoint saved' : 'Endpoint added');
      endpointForm = null;
      endpoints = await api.get<Endpoint[]>(`/api/destinations/${form.id}/endpoints`);
    } catch (e) {
      notify.fail(e, 'endpoint-save');
    }
  }

  async function deleteEndpoint(ep: Endpoint) {
    if (
      !form?.id ||
      !(await confirm({
        title: `Delete endpoint ${ep.name}?`,
        message: 'Profiles that use this endpoint fall back to the base configuration.',
        confirm: 'Delete endpoint',
        danger: true,
      }))
    )
      return;
    try {
      await api.del(`/api/destinations/${form.id}/endpoints/${ep.id}`);
      notify.success('Endpoint deleted');
      endpoints = await api.get<Endpoint[]>(`/api/destinations/${form.id}/endpoints`);
    } catch (e) {
      notify.fail(e);
    }
  }

  function fieldId(f: DestField) {
    return `d-${f.key}`;
  }
</script>

{#snippet configField(f: DestField)}
  {#if f.kind === 'bool'}
    <label class="check"><input type="checkbox" bind:checked={form!.config[f.key]} />{f.label}</label>
  {:else}
    <div class="field">
      <label for={fieldId(f)} use:tooltip={f.hint ?? null}>{f.label}</label>
      {#if f.kind === 'select'}
        <select id={fieldId(f)} bind:value={form!.config[f.key]}
          >{#each f.options ?? [] as [value, label]}<option {value}>{label}</option>{/each}</select
        >
      {:else if f.kind === 'textarea'}
        <textarea
          id={fieldId(f)}
          rows={f.key === 'PrivateKey' ? 8 : 3}
          spellcheck="false"
          placeholder={form!.config[`__secret_${f.key}`] ? 'Stored. Leave empty to keep the stored value.' : f.placeholder}
          bind:value={form!.config[f.key]}></textarea>
      {:else}
        <input
          id={fieldId(f)}
          type={f.kind === 'number'
            ? 'number'
            : f.kind === 'password'
              ? 'password'
              : f.kind === 'email'
                ? 'email'
                : f.kind === 'url'
                  ? 'url'
                  : 'text'}
          autocomplete={f.secret ? 'new-password' : 'off'}
          placeholder={form!.config[`__secret_${f.key}`] ? 'Stored. Leave empty to keep the stored value.' : f.placeholder}
          bind:value={form!.config[f.key]}
        />
      {/if}
    </div>
  {/if}
{/snippet}

<Page crumbs={[{ label: 'Destinations' }]}>
  {#snippet actions()}
    <button type="button" class="btn primary" onclick={create}><Plus size={15} />New destination</button>
  {/snippet}
  <div class="page-body">
    <div class="toolbar">
      <input type="search" placeholder="Search name, description or tag" aria-label="Search destinations" bind:value={search} />
      <select aria-label="Type" bind:value={typeFilter}>
        <option value="">All types</option>
        {#each DESTINATION_TYPES as [value, label]}<option {value}>{label}</option>{/each}
      </select>
      <span class="spacer"></span>
      <span class="muted"><span class="num">{visible.length}</span> of <span class="num">{list.length}</span></span>
    </div>
    <div class="table-wrap">
      <table class="data">
        <thead
          ><tr><th>Name</th><th>Type</th><th>Description</th><th>Tags</th><th>Status</th><th><span class="sr">Actions</span></th></tr
          ></thead
        >
        <tbody>
          {#each visible as d (d.id)}
            <tr
              class="clickable"
              onclick={(e) => {
                if (!(e.target as Element).closest('button')) setQuery('id', String(d.id));
              }}
            >
              <td class="strong">{d.name}</td>
              <td>{TYPE_LABEL[d.type] ?? d.type}</td>
              <td class="muted trunc">{d.description ?? ''}</td>
              <td
                >{(d.tags ?? '')
                  .split(',')
                  .map((t: string) => t.trim())
                  .filter(Boolean)
                  .join(', ')}</td
              >
              <td><StatusMark kind={d.isActive ? 'ok' : 'off'} label={d.isActive ? 'On' : 'Off'} /></td>
              <td class="actions">
                <button
                  type="button"
                  class="btn ghost icon"
                  aria-label="Edit {d.name}"
                  use:tooltip={'Edit'}
                  onclick={() => setQuery('id', String(d.id))}><Pencil size={15} /></button
                >
                <button
                  type="button"
                  class="btn ghost icon danger"
                  aria-label="Delete {d.name}"
                  use:tooltip={'Delete'}
                  onclick={() => remove(d)}><Trash2 size={15} /></button
                >
              </td>
            </tr>
          {:else}
            <tr
              ><td colspan="6" class="empty">
                {#if !loaded}Loading destinations
                {:else if list.length === 0}
                  <div class="empty-state">
                    <h2>No destinations</h2>
                    <p>A destination receives the files and emails that profiles produce.</p>
                    <button type="button" class="btn primary" onclick={create}>New destination</button>
                  </div>
                {:else}No destination matches the search.{/if}
              </td></tr
            >
          {/each}
        </tbody>
      </table>
    </div>
  </div>
</Page>

{#if form}
  <Sheet title={form.id ? form.name || 'Destination' : 'New destination'} onclose={close}>
    <form id="destination-form" class="fields" onsubmit={save}>
      <div class="field"><label for="d-name">Name</label><input id="d-name" bind:value={form.name} autocomplete="off" /></div>
      <div class="field">
        <label for="d-type">Type</label>
        <select id="d-type" value={form.type} onchange={(e) => changeType(e.currentTarget.value)}>
          {#each DESTINATION_TYPES as [value, label]}<option {value}>{label}</option>{/each}
        </select>
      </div>
      <div class="field"><label for="d-desc">Description</label><input id="d-desc" bind:value={form.description} /></div>
      <div class="field">
        <label for="d-tags">Tags</label><input
          id="d-tags"
          bind:value={form.tags}
          placeholder="production, client-a"
          use:tooltip={'Comma-separated'}
        />
      </div>

      <h3 class="section">Configuration</h3>
      {#each fields.filter((f) => !f.advanced) as f (f.key)}{@render configField(f)}{/each}
      {#if fields.some((f) => f.advanced)}
        <details bind:open={advanced}>
          <summary>Advanced settings</summary>
          <div class="fields inner">
            {#each fields.filter((f) => f.advanced) as f (f.key)}{@render configField(f)}{/each}
          </div>
        </details>
      {/if}
    </form>

    {#if hasEndpoints}
      <section class="fields">
        <h3 class="section">Endpoints</h3>
        <p class="muted">Endpoints override the base configuration per profile.</p>
        {#each endpoints as ep (ep.id)}
          <div class="ep">
            <span class="grow"
              >{#if ep.method}<span class="num">{ep.method}</span>
              {/if}<strong>{ep.name}</strong> <span class="muted">{ep.pathSuffix || ep.remotePath || ep.keyPrefix || ''}</span></span
            >
            {#if form.type === 'Http'}<button type="button" class="link" onclick={() => testEndpoint(ep)}>Test</button>{/if}
            <button type="button" class="link" onclick={() => (endpointForm = { ...ep })}>Edit</button>
            <button type="button" class="link danger" onclick={() => deleteEndpoint(ep)}>Delete</button>
          </div>
        {/each}
        {#if endpointForm}
          <div class="panel panel-body fields">
            <div class="field"><label for="ep-name">Name</label><input id="ep-name" bind:value={endpointForm.name} /></div>
            {#if form.type === 'Http'}
              <div class="grid2">
                <div class="field">
                  <label for="ep-path">Path suffix</label><input id="ep-path" placeholder="orders" bind:value={endpointForm.pathSuffix} />
                </div>
                <div class="field">
                  <label for="ep-method">Method</label><select id="ep-method" bind:value={endpointForm.method}
                    ><option value="">Inherit, POST</option>{#each ['GET', 'POST', 'PUT', 'PATCH', 'DELETE'] as m}<option value={m}
                        >{m}</option
                      >{/each}</select
                  >
                </div>
              </div>
              <div class="field">
                <label for="ep-format">Upload format</label><select id="ep-format" bind:value={endpointForm.uploadFormat}
                  ><option value="">Inherit</option><option value="raw">Raw binary</option><option value="multipart">Multipart</option
                  ><option value="json">JSON</option></select
                >
              </div>
              <div class="field">
                <label for="ep-headers">Extra headers in JSON</label><textarea
                  id="ep-headers"
                  rows="2"
                  bind:value={endpointForm.extraHeaders}></textarea>
              </div>
            {:else if form.type === 'S3'}
              <div class="field"><label for="ep-prefix">Key prefix</label><input id="ep-prefix" bind:value={endpointForm.keyPrefix} /></div>
            {:else}
              <div class="field">
                <label for="ep-remote">Remote path</label><input id="ep-remote" bind:value={endpointForm.remotePath} />
              </div>
            {/if}
            <div class="row">
              <button type="button" class="btn primary" onclick={saveEndpoint}>Save endpoint</button>
              {#if form.type === 'Http'}<button type="button" class="btn" onclick={() => testEndpoint(endpointForm!)}>Test</button>{/if}
              <button type="button" class="btn ghost" onclick={() => (endpointForm = null)}>Cancel</button>
            </div>
          </div>
        {:else}
          <div>
            <button type="button" class="btn" onclick={() => (endpointForm = { id: 0, name: '' })}><Plus size={14} />Add endpoint</button>
          </div>
        {/if}
      </section>
    {/if}

    {#snippet footer()}
      {#if form?.id}
        <button type="button" class="btn danger" onclick={() => remove({ id: form!.id!, name: form!.name })}>Delete</button>
        <button type="button" class="btn" onclick={toggle}>{form.isActive ? 'Turn off' : 'Turn on'}</button>
      {/if}
      <span class="grow"></span>
      <button
        type="button"
        class="btn"
        aria-busy={busy === 'test'}
        disabled={form?.type === 'Http' && endpoints.length > 0}
        use:tooltip={form?.type === 'Http' && endpoints.length > 0 ? 'Test each endpoint from its row' : null}
        onclick={() => testConfig(toStored(form!.type, form!.config), form!.name || 'Destination')}>Test connection</button
      >
      <button type="submit" form="destination-form" class="btn primary" aria-busy={busy === 'save'}>Save</button>
    {/snippet}
  </Sheet>
{/if}

<style>
  .strong {
    font-weight: 500;
  }
  .trunc {
    max-width: 320px;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  .section {
    margin-top: 8px;
    padding-top: 16px;
    border-top: 1px solid var(--line);
  }
  details summary {
    cursor: pointer;
    color: var(--soft);
  }
  .inner {
    margin-top: 14px;
  }
  .ep {
    display: flex;
    align-items: center;
    gap: 12px;
    padding: 8px 0;
    border-bottom: 1px solid var(--line);
  }
  .grow {
    flex: 1;
    min-width: 0;
  }
  .row {
    display: flex;
    gap: 8px;
  }
  .danger {
    color: var(--err);
  }
</style>
