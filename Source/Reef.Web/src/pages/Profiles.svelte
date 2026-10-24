<script lang="ts">
  import { ChevronDown, ChevronsUpDown, ChevronUp, Download, Pause, Pencil, Play, Plus, Power, Trash2 } from '@lucide/svelte';
  import Page from '../components/Page.svelte';
  import StatusMark from '../components/StatusMark.svelte';
  import { api } from '../lib/api';
  import type { Connection } from '../models/Connection';
  import type { Destination } from '../models/Destination';
  import type { ExportProfile } from '../models/ExportProfile';
  import type { ImportProfile } from '../models/ImportProfile';
  import type { ProfileGroup } from '../models/ProfileGroup';
  import type { QueryTemplate } from '../models/QueryTemplate';
  import { confirm } from '../lib/confirm.svelte';
  import { formatDateTime, formatRelative, parseDate } from '../lib/format';
  import { notify } from '../lib/notify';
  import { runProfile, type ProfileKind } from '../lib/profiles';
  import { forget } from '../lib/recent.svelte';
  import { navigate } from '../lib/router.svelte';
  import { tooltip } from '../lib/tooltip';

  const SOURCE_LABELS: Record<string, string> = {
    Local: 'Local file',
    Sftp: 'SFTP',
    Http: 'HTTP API',
    S3: 'S3',
    AzureBlob: 'Azure Blob',
    NetworkShare: 'Network share',
  };
  const FILTERS_KEY = 'reef_profiles_filters_v2';
  const SORT_KEY = 'reef_profiles_sort';

  interface Row {
    kind: ProfileKind;
    id: string;
    name: string;
    code?: string;
    enabled: boolean;
    lastRun?: string | null;
    groupId?: string | null;
    group: string;
    connectionId?: string | null;
    source: string;
    sourceSub: string;
    sourceWarn?: boolean;
    format: string;
    destination: string;
    destinationSub: string;
    isEmail?: boolean;
    runBlocked: string | null;
  }

  type SortColumn = 'name' | 'source' | 'group' | 'format' | 'destination' | 'status' | 'lastrun';

  function stored<T extends object>(key: string, fallback: T): T {
    try {
      const saved = JSON.parse(localStorage.getItem(key) ?? 'null');
      return saved && typeof saved === 'object' ? { ...fallback, ...saved } : fallback;
    } catch {
      return fallback;
    }
  }

  let rows = $state<Row[]>([]);
  let connections = $state<{ id: string; name: string }[]>([]);
  let groups = $state<{ id: string; name: string }[]>([]);
  let loaded = $state(false);
  let busy = $state<string | null>(null);
  let filters = $state(stored(FILTERS_KEY, { search: '', kind: '', status: '', connection: '', group: '' }));
  let sort = $state<{ column: SortColumn; direction: 'asc' | 'desc' }>(stored(SORT_KEY, { column: 'name', direction: 'asc' }));

  $effect(() => {
    try {
      localStorage.setItem(FILTERS_KEY, JSON.stringify(filters));
      localStorage.setItem(SORT_KEY, JSON.stringify(sort));
    } catch {}
  });

  function exportRow(p: ExportProfile, destinations: Destination[], templates: QueryTemplate[]): Row {
    const destination = destinations.find((d) => d.id === p.outputDestinationId);
    const template = p.templateId ? templates.find((t) => t.id === p.templateId) : null;
    return {
      kind: 'export',
      id: p.id,
      name: p.name,
      code: p.code,
      enabled: p.isEnabled,
      lastRun: p.lastExecutedAt,
      groupId: p.groupId,
      group: p.groupName || '',
      connectionId: p.connectionId,
      source: p.connectionName || 'No connection',
      sourceSub: p.connectionIsActive === false ? 'Connection is off' : 'Query',
      sourceWarn: p.connectionIsActive === false,
      format: p.isEmailExport ? 'Email' : template?.outputFormat || p.outputFormat || '',
      destination: destination?.name || 'Not set up',
      destinationSub: destination?.type || '',
      isEmail: p.isEmailExport,
      runBlocked: !p.isEnabled
        ? 'Turn the profile on to run it'
        : p.connectionIsActive === false
          ? 'Turn the connection on to run it'
          : null,
    };
  }

  function importRow(p: ImportProfile): Row {
    const database = p.targetType !== 'LocalFile';
    return {
      kind: 'import',
      id: p.id,
      name: p.name,
      code: p.code,
      enabled: p.isEnabled,
      lastRun: p.lastExecutedAt,
      groupId: p.groupId,
      group: p.groupName || '',
      connectionId: p.targetConnectionId,
      source: p.sourceDestinationName || SOURCE_LABELS[p.sourceType] || p.sourceType || '',
      sourceSub: 'Import',
      format: p.sourceFormat || '',
      destination: database ? p.targetConnectionName || 'Not set up' : 'Local file',
      destinationSub: database ? p.targetTable || '' : p.localTargetFormat || '',
      runBlocked: p.isEnabled ? null : 'Turn the profile on to run it',
    };
  }

  async function load() {
    const fail = (e: unknown) => {
      notify.fail(e, 'profiles-load');
      return [];
    };
    const [exports, imports, destinations, templates, conns, grps] = await Promise.all([
      api.get<ExportProfile[]>('/api/profiles').catch(fail),
      api.get<ImportProfile[]>('/api/profiles?type=import').catch(fail),
      api.get<Destination[]>('/api/destinations').catch(fail),
      api.get<QueryTemplate[]>('/api/templates').catch(fail),
      api.get<Connection[]>('/api/connections').catch(fail),
      api.get<ProfileGroup[]>('/api/groups').catch(fail),
    ]);
    rows = [...(exports ?? []).map((p) => exportRow(p, destinations ?? [], templates ?? [])), ...(imports ?? []).map(importRow)];
    connections = conns ?? [];
    groups = grps ?? [];
    loaded = true;
  }

  $effect(() => {
    load();
  });

  const visible = $derived.by(() => {
    const term = filters.search.trim().toLowerCase();
    const sign = sort.direction === 'asc' ? 1 : -1;
    const key = (row: Row): string | number =>
      sort.column === 'lastrun'
        ? (parseDate(row.lastRun)?.getTime() ?? 0)
        : sort.column === 'status'
          ? row.enabled
            ? 0
            : 1
          : String(row[sort.column] ?? '').toLowerCase();
    return rows
      .filter((row) => !filters.kind || row.kind === filters.kind)
      .filter((row) => !filters.status || (filters.status === 'enabled') === row.enabled)
      .filter((row) => !filters.connection || String(row.connectionId) === filters.connection)
      .filter((row) => !filters.group || String(row.groupId) === filters.group)
      .filter(
        (row) =>
          !term ||
          [row.name, row.code, row.source, row.group, row.format, row.destination, row.destinationSub].some((v) =>
            String(v ?? '')
              .toLowerCase()
              .includes(term),
          ),
      )
      .sort((a, b) => {
        const x = key(a),
          y = key(b);
        return (x < y ? -1 : x > y ? 1 : 0) * sign || a.name.localeCompare(b.name);
      });
  });

  const filtered = $derived(Object.values(filters).some(Boolean));

  function toggleSort(column: SortColumn) {
    sort = { column, direction: sort.column === column && sort.direction === 'asc' ? 'desc' : 'asc' };
  }

  function clearFilters() {
    filters = { search: '', kind: '', status: '', connection: '', group: '' };
  }

  async function act(row: Row, action: string, fn: () => Promise<void>) {
    busy = `${action}-${row.kind}-${row.id}`;
    try {
      await fn();
    } catch (e) {
      notify.fail(e);
    } finally {
      busy = null;
    }
  }

  async function run(row: Row) {
    if (
      row.isEmail &&
      !(await confirm({
        title: `Send emails for ${row.name}?`,
        message: 'The profile sends one email per row, or per group, to the recipients in the query result.',
        confirm: 'Send emails',
        danger: true,
      }))
    )
      return;
    await act(row, 'run', async () => {
      await runProfile(row.kind, row.id, row.name);
      await load();
    });
  }

  async function downloadJson(row: Row) {
    await act(row, 'download', async () => {
      const doc = await api.get(`/api/profile-documents/${row.kind}/${row.id}`);
      const a = document.createElement('a');
      a.href = URL.createObjectURL(new Blob([JSON.stringify(doc, null, 2)], { type: 'application/json' }));
      a.download = `${row.name.replace(/[^\w.-]+/g, '_')}.json`;
      a.click();
      URL.revokeObjectURL(a.href);
    });
  }

  async function toggle(row: Row) {
    await act(row, 'toggle', async () => {
      await api.post(`/api/${row.kind === 'import' ? 'import-profiles' : 'profiles'}/${row.id}/${row.enabled ? 'disable' : 'enable'}`);
      notify.success(`${row.name} turned ${row.enabled ? 'off' : 'on'}`);
      await load();
    });
  }

  async function remove(row: Row) {
    if (
      !(await confirm({
        title: `Delete ${row.name}?`,
        message: 'Deleted profiles cannot be restored.',
        confirm: 'Delete profile',
        danger: true,
      }))
    )
      return;
    await act(row, 'delete', async () => {
      await api.del(`/api/profiles/${row.id}${row.kind === 'import' ? '?type=import' : ''}`);
      forget(row.kind, row.id);
      notify.success(`${row.name} deleted`);
      await load();
    });
  }

  const columns: { key: SortColumn; label: string }[] = [
    { key: 'name', label: 'Name' },
    { key: 'source', label: 'Source' },
    { key: 'group', label: 'Group' },
    { key: 'format', label: 'Format' },
    { key: 'destination', label: 'Destination' },
    { key: 'status', label: 'Status' },
    { key: 'lastrun', label: 'Last run' },
  ];
</script>

<Page crumbs={[{ label: 'Profiles' }]}>
  {#snippet actions()}
    <a class="btn" href="/profiles/import/new"><Plus size={15} />Import profile</a>
    <a class="btn primary" href="/profiles/export/new"><Plus size={15} />Export profile</a>
  {/snippet}
  <div class="page-body">
    <div class="toolbar">
      <input
        type="search"
        placeholder="Search name, code, connection, group or format"
        aria-label="Search profiles"
        bind:value={filters.search}
      />
      <select aria-label="Kind" bind:value={filters.kind}>
        <option value="">All kinds</option><option value="export">Export</option><option value="import">Import</option>
      </select>
      <select aria-label="Status" bind:value={filters.status}>
        <option value="">All statuses</option><option value="enabled">On</option><option value="disabled">Off</option>
      </select>
      <select aria-label="Connection" bind:value={filters.connection}>
        <option value="">All connections</option>
        {#each connections as c (c.id)}<option value={String(c.id)}>{c.name}</option>{/each}
      </select>
      <select aria-label="Group" bind:value={filters.group}>
        <option value="">All groups</option>
        {#each groups as g (g.id)}<option value={String(g.id)}>{g.name}</option>{/each}
      </select>
      {#if filtered}<button type="button" class="link" onclick={clearFilters}>Clear filters</button>{/if}
      <span class="spacer"></span>
      <span class="muted" aria-live="polite"><span class="num">{visible.length}</span> of <span class="num">{rows.length}</span></span>
    </div>

    <div class="table-wrap">
      <table class="data">
        <thead>
          <tr>
            {#each columns as column (column.key)}
              <th aria-sort={sort.column === column.key ? (sort.direction === 'asc' ? 'ascending' : 'descending') : 'none'}>
                <button type="button" onclick={() => toggleSort(column.key)}>
                  {column.label}
                  {#if sort.column !== column.key}<ChevronsUpDown size={12} />{:else if sort.direction === 'asc'}<ChevronUp
                      size={12}
                    />{:else}<ChevronDown size={12} />{/if}
                </button>
              </th>
            {/each}
            <th><span class="sr">Actions</span></th>
          </tr>
        </thead>
        <tbody>
          {#each visible as row (row.kind + row.id)}
            <tr
              class="clickable"
              onclick={(e) => {
                if (!(e.target as Element).closest('button, a')) navigate(`/profiles/${row.kind}/${row.id}`);
              }}
            >
              <td>
                <a class="name" href="/profiles/{row.kind}/{row.id}">{row.name}</a>
                <span class="sub"
                  >{row.kind === 'import' ? 'Import' : 'Export'}{#if row.code}
                    · <span class="num">{row.code}</span>{/if}</span
                >
              </td>
              <td><span class="name">{row.source}</span><span class="sub" class:warn={row.sourceWarn}>{row.sourceSub}</span></td>
              <td>{row.group || 'None'}</td>
              <td>{row.format}</td>
              <td><span class="name">{row.destination}</span><span class="sub">{row.destinationSub}</span></td>
              <td><StatusMark kind={row.enabled ? 'ok' : 'off'} label={row.enabled ? 'On' : 'Off'} /></td>
              <td><span use:tooltip={formatDateTime(row.lastRun)}>{row.lastRun ? formatRelative(row.lastRun) : 'Never'}</span></td>
              <td class="actions">
                <button
                  type="button"
                  class="btn ghost icon"
                  disabled={!!row.runBlocked}
                  aria-busy={busy === `run-${row.kind}-${row.id}`}
                  aria-label="Run {row.name}"
                  use:tooltip={row.runBlocked ?? (row.isEmail ? 'Run and send emails' : 'Run now')}
                  onclick={() => run(row)}><Play size={15} /></button
                >
                <a class="btn ghost icon" href="/profiles/{row.kind}/{row.id}" aria-label="Edit {row.name}" use:tooltip={'Edit'}
                  ><Pencil size={15} /></a
                >
                <button
                  type="button"
                  class="btn ghost icon"
                  aria-label="Download {row.name} as JSON"
                  use:tooltip={'Download JSON'}
                  onclick={() => downloadJson(row)}><Download size={15} /></button
                >
                <button
                  type="button"
                  class="btn ghost icon"
                  aria-label="{row.enabled ? 'Turn off' : 'Turn on'} {row.name}"
                  use:tooltip={row.enabled ? 'Turn off' : 'Turn on'}
                  onclick={() => toggle(row)}
                >
                  {#if row.enabled}<Pause size={15} />{:else}<Power size={15} />{/if}
                </button>
                <button
                  type="button"
                  class="btn ghost icon danger"
                  aria-label="Delete {row.name}"
                  use:tooltip={'Delete'}
                  onclick={() => remove(row)}><Trash2 size={15} /></button
                >
              </td>
            </tr>
          {:else}
            <tr>
              <td colspan="8" class="empty">
                {#if !loaded}
                  Loading profiles
                {:else if rows.length === 0}
                  <div class="empty-state">
                    <h2>No profiles</h2>
                    <p>
                      An export profile queries a database and writes the rows to a destination. An import profile reads a file or API into
                      a table.
                    </p>
                    <div class="toolbar">
                      <a class="btn primary" href="/profiles/export/new">New export profile</a>
                      <a class="btn" href="/profiles/import/new">New import profile</a>
                    </div>
                  </div>
                {:else}
                  No profile matches the search and filters. <button type="button" class="link" onclick={clearFilters}>Clear filters</button
                  >
                {/if}
              </td>
            </tr>
          {/each}
        </tbody>
      </table>
    </div>
  </div>
</Page>

<style>
  .name {
    display: block;
    color: var(--ink);
    font-weight: 500;
    text-decoration: none;
  }
  a.name:hover {
    text-decoration: underline;
  }
  .sub {
    display: block;
    font-size: 12px;
    color: var(--muted);
    margin-top: 1px;
  }
  .sub.warn {
    color: var(--warn);
  }
</style>
