<script lang="ts">
  import { ChevronLeft, ChevronRight, Play, RefreshCw } from '@lucide/svelte';
  import Page from '../components/Page.svelte';
  import Sheet from '../components/Sheet.svelte';
  import StatusMark from '../components/StatusMark.svelte';
  import ExportDetail from '../components/executions/ExportDetail.svelte';
  import ImportDetail from '../components/executions/ImportDetail.svelte';
  import { api } from '../lib/api';
  import { queryString } from '../lib/query';
  import { formatDateTime, formatDuration, formatNumber, formatRelative, parseDate } from '../lib/format';
  import { notify } from '../lib/notify';
  import { match, navigate, route } from '../lib/router.svelte';
  import { urlPaging } from '../lib/paging.svelte';
  import type { ExecuteResult } from '../models/ExecuteResult';
  import type { ExportExecution } from '../models/ExportExecution';
  import type { ExportProfile } from '../models/ExportProfile';
  import type { ImportExecution } from '../models/ImportExecution';
  import type { ImportProfile } from '../models/ImportProfile';
  import type { Job } from '../models/Job';
  import type { PagedResult } from '../models/PagedResult';
  import { statusKind, statusLabel } from '../lib/status';
  import { tooltip } from '../lib/tooltip';

  const FILTERS_KEY = 'executions_filters';

  interface Filters {
    profileId: string;
    jobId: string;
    status: string;
    fromDate: string;
    toDate: string;
    searchTerm: string;
  }

  function storedFilters(): Filters {
    const empty = { profileId: '', jobId: '', status: '', fromDate: '', toDate: '', searchTerm: '' };
    try {
      return { ...empty, ...JSON.parse(localStorage.getItem(FILTERS_KEY) ?? '{}') };
    } catch {
      return empty;
    }
  }

  let filters = $state<Filters>(storedFilters());
  const paging = urlPaging(25);
  let total = $state(0);
  let pages = $state(1);
  let exportsList = $state<ExportExecution[]>([]);
  let importsList = $state<(ImportExecution & { _name: string; _code: string })[]>([]);
  let profiles = $state<ExportProfile[]>([]);
  let importProfiles = $state<ImportProfile[]>([]);
  let jobs = $state<Job[]>([]);
  let loaded = $state(false);
  let runDialog: HTMLDialogElement;
  let runMode = $state<'profile' | 'job'>('profile');
  let runTarget = $state('');
  let running = $state(false);

  $effect(() => {
    const profile = route.query.get('profile');
    const job = route.query.get('job');
    if (profile) filters.profileId = profile.startsWith('export:') ? profile.slice('export:'.length) : profile;
    if (job) filters.jobId = job;
  });

  $effect(() => {
    try {
      localStorage.setItem(FILTERS_KEY, JSON.stringify(filters));
    } catch {}
  });

  async function loadLookups() {
    [profiles, importProfiles, jobs] = await Promise.all([
      api.get<ExportProfile[]>('/api/profiles').catch(() => []),
      api.get<ImportProfile[]>('/api/profiles?type=import').catch(() => []),
      api.get<Job[]>('/api/jobs').catch(() => []),
    ]);
    await loadImports();
  }

  async function loadImports() {
    const lists = await Promise.all(
      importProfiles.map((p) =>
        api
          .get<ImportExecution[]>(`/api/profiles/${p.id}/executions?type=import&limit=10`)
          .then((list) => (list ?? []).map((e) => ({ ...e, _name: p.name, _code: p.code })))
          .catch(() => []),
      ),
    );
    importsList = lists.flat();
  }

  async function loadExports() {
    if (filters.profileId.startsWith('import:')) {
      exportsList = [];
      loaded = true;
      return;
    }
    const q = queryString({
      page: paging.page,
      pageSize: paging.size,
      profileId: filters.profileId,
      jobId: filters.jobId,
      status: filters.status,
      fromDate: filters.fromDate,
      toDate: filters.toDate,
      search: filters.searchTerm,
    });
    try {
      const result = await api.get<PagedResult<ExportExecution>>(`/api/executions?${q}`);
      exportsList = result.data ?? [];
      total = result.totalCount ?? exportsList.length;
      pages = Math.max(1, result.totalPages ?? 1);
    } catch (e) {
      notify.fail(e, 'executions');
    }
    loaded = true;
  }

  $effect(() => {
    loadLookups();
  });

  $effect(() => {
    loadExports();
  });

  $effect(() => {
    const timer = setInterval(() => {
      if (exportsList.some((e) => e.status === 'Running') || importsList.some((e) => e.status === 'Running')) {
        loadExports();
        loadImports();
      }
    }, 10000);
    return () => clearInterval(timer);
  });

  function setFilter<K extends keyof Filters>(key: K, value: Filters[K]) {
    filters[key] = value;
    paging.page = 1;
  }

  const rows = $derived.by(() => {
    const term = filters.searchTerm.trim().toLowerCase();
    const importOnly = filters.profileId.startsWith('import:') ? Number(filters.profileId.split(':')[1]) : null;
    const exportFiltered = (!!filters.profileId && !importOnly) || !!filters.jobId;
    let imports = importOnly !== null ? importsList.filter((e) => e.importProfileId === importOnly) : exportFiltered ? [] : importsList;
    if (term) imports = imports.filter((e) => `${e._name} ${e._code ?? ''}`.toLowerCase().includes(term));
    if (filters.status) imports = imports.filter((e) => e.status === filters.status);
    const time = (e: { startedAt: string }) => parseDate(e.startedAt)?.getTime() ?? 0;
    return [
      ...exportsList.map((e) => ({
        kind: 'export' as const,
        id: e.id,
        name: profiles.find((p) => p.id === e.profileId)?.name ?? e.profileName ?? `Profile ${e.profileId}`,
        code: e.profileCode,
        startedAt: e.startedAt,
        durationMs: e.executionTimeMs,
        status: e.status,
        rows: e.rowCount ?? 0,
        triggeredBy: e.triggeredBy || 'System',
        pre: e.preProcessStatus,
        post: e.postProcessStatus,
      })),
      ...imports.map((e) => ({
        kind: 'import' as const,
        id: e.id,
        name: e._name,
        code: e._code,
        startedAt: e.startedAt,
        durationMs: e.completedAt ? parseDate(e.completedAt)!.getTime() - parseDate(e.startedAt)!.getTime() : null,
        status: e.status,
        rows: e.totalRowsRead ?? (e.rowsInserted ?? 0) + (e.rowsUpdated ?? 0),
        triggeredBy: e.triggeredBy || 'System',
        pre: null,
        post: null,
      })),
    ].sort((a, b) => time(b) - time(a));
  });

  const filtered = $derived(Object.values(filters).some(Boolean));
  const detail = $derived.by(() => {
    const params = match('/executions/:kind/:id', route.path);
    return params && (params.kind === 'export' || params.kind === 'import') ? { kind: params.kind, id: Number(params.id) } : null;
  });

  function open(row: { kind: string; id: number }) {
    navigate(`/executions/${row.kind}/${row.id}${location.search}`);
  }

  function closeDetail() {
    navigate(`/executions${location.search}`);
  }

  function openRun(mode: 'profile' | 'job') {
    runMode = mode;
    runTarget = '';
    runDialog.showModal();
  }

  async function submitRun(e: SubmitEvent) {
    e.preventDefault();
    running = true;
    try {
      const result =
        runMode === 'profile'
          ? await api.post<ExecuteResult>(`/api/executions/execute/${runTarget}`)
          : await api.post<ExecuteResult>(`/api/jobs/${runTarget}/trigger`);
      notify.success(runMode === 'profile' ? `Execution ${result?.executionId ?? ''} started`.trim() : 'Job triggered');
      runDialog.close();
      await Promise.all([loadExports(), loadImports()]);
    } catch (err) {
      notify.fail(err, 'executions-run');
    } finally {
      running = false;
    }
  }

  function processMark(status: string | null | undefined) {
    return status ? statusKind(status) : null;
  }
</script>

<Page crumbs={[{ label: 'Executions' }]}>
  {#snippet actions()}
    <button
      type="button"
      class="btn ghost icon"
      aria-label="Refresh"
      use:tooltip={'Refresh'}
      onclick={() => {
        loadExports();
        loadImports();
      }}><RefreshCw size={15} /></button
    >
    <button type="button" class="btn" onclick={() => openRun('job')}>Run job</button>
    <button type="button" class="btn primary" onclick={() => openRun('profile')}><Play size={14} />Run profile</button>
  {/snippet}
  <div class="page-body">
    <div class="toolbar">
      <input
        type="search"
        placeholder="Search profile name or code"
        aria-label="Search executions"
        value={filters.searchTerm}
        oninput={(e) => setFilter('searchTerm', e.currentTarget.value)}
      />
      <select aria-label="Profile" value={filters.profileId} onchange={(e) => setFilter('profileId', e.currentTarget.value)}>
        <option value="">All profiles</option>
        <optgroup label="Export profiles"
          >{#each profiles as p (p.id)}<option value={String(p.id)}>{p.name}</option>{/each}</optgroup
        >
        {#if importProfiles.length}<optgroup label="Import profiles"
            >{#each importProfiles as p (p.id)}<option value="import:{p.id}">{p.name}</option>{/each}</optgroup
          >{/if}
      </select>
      <select aria-label="Job" value={filters.jobId} onchange={(e) => setFilter('jobId', e.currentTarget.value)}>
        <option value="">All jobs</option>
        {#each jobs as j (j.id)}<option value={String(j.id)}>{j.name}</option>{/each}
      </select>
      <select aria-label="Status" value={filters.status} onchange={(e) => setFilter('status', e.currentTarget.value)}>
        <option value="">All statuses</option>
        <option value="Success">Success</option><option value="Failed">Failed</option><option value="Running">Running</option><option
          value="PartialSuccess">Partial success</option
        >
      </select>
      <input
        type="date"
        aria-label="From date"
        class="date"
        value={filters.fromDate}
        onchange={(e) => setFilter('fromDate', e.currentTarget.value)}
      />
      <input
        type="date"
        aria-label="To date"
        class="date"
        value={filters.toDate}
        onchange={(e) => setFilter('toDate', e.currentTarget.value)}
      />
      {#if filtered}<button
          type="button"
          class="link"
          onclick={() => {
            filters = { profileId: '', jobId: '', status: '', fromDate: '', toDate: '', searchTerm: '' };
            paging.page = 1;
          }}>Clear filters</button
        >{/if}
    </div>

    <div class="table-wrap">
      <table class="data">
        <thead
          ><tr
            ><th>Profile</th><th>Kind</th><th>Started</th><th>Duration</th><th>Pre</th><th>Post</th><th>Status</th><th>Rows</th><th
              >Triggered by</th
            ></tr
          ></thead
        >
        <tbody>
          {#each rows as row (row.kind + row.id)}
            <tr class="clickable" onclick={() => open(row)}>
              <td
                ><span class="strong">{row.name}</span>{#if row.code}<span class="num muted code-ref">{row.code}</span>{/if}</td
              >
              <td>{row.kind === 'import' ? 'Import' : 'Export'}</td>
              <td><span use:tooltip={formatDateTime(row.startedAt)}>{formatRelative(row.startedAt)}</span></td>
              <td class="num">{formatDuration(row.durationMs)}</td>
              <td
                >{#if processMark(row.pre)}<span use:tooltip={statusLabel(row.pre)}><StatusMark kind={processMark(row.pre)!} /></span
                  >{/if}</td
              >
              <td
                >{#if processMark(row.post)}<span use:tooltip={statusLabel(row.post)}><StatusMark kind={processMark(row.post)!} /></span
                  >{/if}</td
              >
              <td><StatusMark kind={statusKind(row.status)} label={statusLabel(row.status)} /></td>
              <td class="num">{formatNumber(row.rows)}</td>
              <td>{row.triggeredBy}</td>
            </tr>
          {:else}
            <tr><td colspan="9" class="empty">{loaded ? 'No executions match the filters.' : 'Loading executions'}</td></tr>
          {/each}
        </tbody>
      </table>
    </div>

    <div class="pager">
      <span class="muted"><span class="num">{formatNumber(total)}</span> export executions</span>
      <span class="spacer"></span>
      <label class="inline" for="page-size">Rows per page</label>
      <select id="page-size" class="size" value={String(paging.size)} onchange={(e) => (paging.size = Number(e.currentTarget.value))}>
        {#each [10, 25, 50, 100] as n (n)}<option value={String(n)}>{n}</option>{/each}
      </select>
      <button type="button" class="btn icon" aria-label="Previous page" disabled={paging.page <= 1} onclick={() => paging.page--}
        ><ChevronLeft size={15} /></button
      >
      <span class="num">{paging.page} / {pages}</span>
      <button type="button" class="btn icon" aria-label="Next page" disabled={paging.page >= pages} onclick={() => paging.page++}
        ><ChevronRight size={15} /></button
      >
    </div>
  </div>
</Page>

{#if detail}
  <Sheet title={detail.kind === 'import' ? 'Import execution' : 'Execution'} onclose={closeDetail} wide>
    {#if detail.kind === 'import'}
      <ImportDetail id={detail.id} profiles={importProfiles} />
    {:else}
      <ExportDetail id={detail.id} {profiles} {jobs} />
    {/if}
  </Sheet>
{/if}

<dialog bind:this={runDialog} aria-labelledby="run-title">
  <form onsubmit={submitRun}>
    <div class="dbody fields">
      <h2 id="run-title">{runMode === 'profile' ? 'Run profile' : 'Run job'}</h2>
      <div class="field">
        <label for="run-target">{runMode === 'profile' ? 'Export profile' : 'Job'}</label>
        <select id="run-target" bind:value={runTarget}>
          <option value="">Choose</option>
          {#each runMode === 'profile' ? profiles : jobs as item (item.id)}<option value={String(item.id)}>{item.name}</option>{/each}
        </select>
      </div>
    </div>
    <div class="dfoot">
      <button type="button" class="btn" onclick={() => runDialog.close()}>Cancel</button>
      <button type="submit" class="btn primary" aria-busy={running}>Run</button>
    </div>
  </form>
</dialog>

<style>
  .strong {
    font-weight: 500;
  }
  .date {
    width: 150px !important;
  }
  .pager {
    display: flex;
    align-items: center;
    gap: 8px;
  }
  .spacer {
    flex: 1;
  }
  .inline {
    margin: 0;
    font-weight: 400;
    color: var(--muted);
  }
  .size {
    width: auto !important;
  }
  .dbody {
    padding: 20px;
    min-width: 380px;
  }
  .dfoot {
    display: flex;
    justify-content: flex-end;
    gap: 8px;
    padding: 12px 20px;
    border-top: 1px solid var(--line);
  }
</style>
