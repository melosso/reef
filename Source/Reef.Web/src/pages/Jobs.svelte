<script lang="ts">
  import { Activity, CircleCheck, Copy, History, Pencil, Play, Plus, RotateCcw, Trash2, TriangleAlert, X } from '@lucide/svelte';
  import Page from '../components/Page.svelte';
  import Sheet from '../components/Sheet.svelte';
  import StatusMark from '../components/StatusMark.svelte';
  import type { StatusKind } from '../components/StatusMark.svelte';
  import { api } from '../lib/api';
  import { confirm } from '../lib/confirm.svelte';
  import { formatDateTime, formatNumber, formatRelative } from '../lib/format';
  import { copyText, notify } from '../lib/notify';
  import { navigate } from '../lib/router.svelte';
  import { detailRoute } from '../lib/detailRoute.svelte';
  import { tooltip } from '../lib/tooltip';
  import type { Connection } from '../models/Connection';
  import type { Destination } from '../models/Destination';
  import type { ExportProfile } from '../models/ExportProfile';
  import type { ImportProfile } from '../models/ImportProfile';
  import type { Job } from '../models/Job';
  import type { ExportExecution } from '../models/ExportExecution';
  import type { JobExecution } from '../models/JobExecution';
  import type { JobOutcome } from '../models/JobOutcome';
  import { jobOutcome } from '../lib/jobOutcome';
  import type { PagedResult } from '../models/PagedResult';
  import type { ProfileGroup } from '../models/ProfileGroup';
  import type { QueueMetrics } from '../models/QueueMetrics';
  import type { Webhook } from '../models/Webhook';

  const STATUS: Record<string, [string, StatusKind]> = {
    0: ['Idle', 'idle'],
    1: ['Queued', 'run'],
    2: ['Running', 'run'],
    3: ['Completed', 'ok'],
    4: ['Failed', 'err'],
    5: ['Cancelled', 'off'],
  };
  const STATUS_NAMES: Record<string, number> = { Idle: 0, Scheduled: 1, Queued: 1, Running: 2, Completed: 3, Failed: 4, Cancelled: 5 };
  const SCHEDULES = ['Manual', 'Cron', 'Interval', 'Daily', 'Weekly', 'Monthly'];
  const SCHEDULE_NAMES: Record<string, number> = { Manual: 0, Cron: 1, CronExpression: 1, Interval: 2, Daily: 3, Weekly: 4, Monthly: 5 };
  const PRIORITIES = ['Low', 'Normal', 'High', 'Critical'];
  const PRIORITY_NAMES: Record<string, number> = { Low: 0, Normal: 1, High: 2, Critical: 3 };
  const DAYS = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];
  const FILTERS_KEY = 'reef_jobs_filters_v2';

  interface Form {
    id?: string;
    name: string;
    description: string;
    type: string;
    profile: string;
    destinationId: string;
    scheduleType: number;
    cronExpression: string;
    intervalMinutes: number;
    startTime: string;
    weekDays: number[];
    monthDay: number;
    priority: number;
    maxRetries: number;
    timeoutMinutes: number;
    allowConcurrent: boolean;
    tags: string;
    isEnabled: boolean;
    autoPauseEnabled: boolean;
  }

  const num = (v: string | number | null | undefined, map: Record<string, number>) =>
    (typeof v === 'string' ? (map[v] ?? Number.parseInt(v, 10)) : v) ?? 0;

  function stored() {
    const empty = { search: '', health: '', status: '', profile: '', connection: '', group: '' };
    try {
      return { ...empty, ...JSON.parse(localStorage.getItem(FILTERS_KEY) ?? '{}') };
    } catch {
      return empty;
    }
  }

  let jobs = $state<Job[]>([]);
  let profiles = $state<ExportProfile[]>([]);
  let importProfiles = $state<ImportProfile[]>([]);
  let destinations = $state<Destination[]>([]);
  let connections = $state<Connection[]>([]);
  let groups = $state<ProfileGroup[]>([]);
  let metrics = $state<QueueMetrics | null>(null);
  let loaded = $state(false);
  let filters = $state(stored());
  let form = $state<Form | null>(null);
  let tab = $state<'settings' | 'webhooks'>('settings');
  let hooks = $state<Webhook[]>([]);
  let revealed = $state('');
  let busy = $state<string | null>(null);

  $effect(() => {
    try {
      localStorage.setItem(FILTERS_KEY, JSON.stringify(filters));
    } catch {}
  });

  async function load() {
    const [j, p, ip, d] = await Promise.all([
      api.get<Job[] | PagedResult<Job>>('/api/jobs').catch((e) => {
        notify.fail(e, 'jobs');
        return [];
      }),
      api.get<ExportProfile[]>('/api/profiles').catch(() => []),
      api.get<ImportProfile[]>('/api/profiles?type=import').catch(() => []),
      api.get<Destination[]>('/api/destinations').catch(() => []),
    ]);
    jobs = Array.isArray(j) ? j : (j?.data ?? []);
    profiles = p ?? [];
    importProfiles = ip ?? [];
    destinations = d ?? [];
    loaded = true;
    const paused = jobs.filter(isPaused).length;
    if (paused)
      notify.warning(`${paused} ${paused === 1 ? 'job is' : 'jobs are'} auto-paused after repeated failures.`, { key: 'jobs-paused' });
  }

  async function loadMetrics() {
    metrics = await api.get<QueueMetrics>('/api/jobs/queue/metrics').catch(() => null);
  }

  $effect(() => {
    load();
    loadMetrics();
    api
      .get<Connection[]>('/api/connections')
      .then((c) => (connections = c ?? []))
      .catch(() => {});
    api
      .get<ProfileGroup[]>('/api/groups')
      .then((g) => (groups = g ?? []))
      .catch(() => {});
    const timer = setInterval(loadMetrics, 15000);
    return () => clearInterval(timer);
  });

  const records = detailRoute('/jobs', { create, edit, dismiss });

  const isPaused = (j: Job) => (j.tags ?? '').includes('circuit-breaker');

  function health(j: Job): { label: string; kind: StatusKind; hint: string } {
    const f = j.consecutiveFailures || 0;
    if (isPaused(j)) return { label: 'Auto-paused', kind: 'err', hint: 'Paused after 10 consecutive failures' };
    if (f >= 7) return { label: 'Failing', kind: 'err', hint: `${f} consecutive failures. Auto-pause follows at 10.` };
    if (f >= 3) return { label: 'Warning', kind: 'warn', hint: `${f} consecutive failures` };
    if (!j.isEnabled) return { label: 'Off', kind: 'off', hint: 'Turned off' };
    return { label: 'Healthy', kind: 'ok', hint: 'No recent failures' };
  }

  function profileOf(j: Job) {
    return profiles.find((p) => p.id === j.profileId) ?? importProfiles.find((p) => p.id === j.importProfileId);
  }

  function schedule(j: Job): string {
    const t = num(j.scheduleType, SCHEDULE_NAMES);
    if (t === 1) return `Cron ${j.cronExpression ?? ''}`;
    if (t === 2) return `Every ${j.intervalMinutes} min`;
    if (t === 3) return `Daily at ${String(j.startTime ?? '').slice(0, 5)}`;
    if (t === 4) return `Weekly at ${String(j.startTime ?? '').slice(0, 5)}`;
    if (t === 5) return `Monthly on day ${j.monthDay ?? 1}`;
    return 'Manual';
  }

  const visible = $derived(
    jobs.filter((j) => {
      const f = j.consecutiveFailures || 0;
      const paused = isPaused(j);
      const h = filters.health;
      if (h === 'auto-paused' && !paused) return false;
      if (h === 'failing' && (f < 7 || paused)) return false;
      if (h === 'warning' && (f < 3 || f >= 7 || paused)) return false;
      if (h === 'healthy' && (f >= 3 || paused || !j.isEnabled)) return false;
      if (h === 'disabled' && j.isEnabled) return false;
      if (filters.status !== '' && num(j.status, STATUS_NAMES) !== Number(filters.status)) return false;
      if (filters.profile && String(j.profileId) !== filters.profile) return false;
      const profile = profiles.find((p) => p.id === j.profileId);
      if (filters.connection && String(profile?.connectionId) !== filters.connection) return false;
      if (filters.group && String(profile?.groupId) !== filters.group) return false;
      const term = filters.search.trim().toLowerCase();
      const linked = profileOf(j);
      return (
        !term ||
        [j.name, linked?.name ?? j.profileName, linked?.code].some((v) =>
          String(v ?? '')
            .toLowerCase()
            .includes(term),
        )
      );
    }),
  );

  function blank(): Form {
    return {
      name: '',
      description: '',
      type: 'ProfileExecution',
      profile: '',
      destinationId: '',
      scheduleType: 0,
      cronExpression: '',
      intervalMinutes: 60,
      startTime: '02:00',
      weekDays: [],
      monthDay: 1,
      priority: 1,
      maxRetries: 3,
      timeoutMinutes: 60,
      allowConcurrent: false,
      tags: '',
      isEnabled: true,
      autoPauseEnabled: true,
    };
  }

  function create() {
    form = blank();
    tab = 'settings';
  }

  async function edit(id: string) {
    try {
      const j = await api.get<Job>(`/api/jobs/${id}`);
      form = {
        id: j.id,
        name: j.name,
        description: j.description ?? '',
        type: j.type ?? 'ProfileExecution',
        profile: j.importProfileId ? `import:${j.importProfileId}` : j.profileId ? `export:${j.profileId}` : '',
        destinationId: j.destinationId ? String(j.destinationId) : '',
        scheduleType: num(j.scheduleType, SCHEDULE_NAMES),
        cronExpression: j.cronExpression ?? '',
        intervalMinutes: j.intervalMinutes ?? 60,
        startTime: String(j.startTime ?? '02:00').slice(0, 5),
        weekDays: String(j.weekDays ?? '')
          .split(',')
          .filter(Boolean)
          .map(Number),
        monthDay: j.monthDay ?? 1,
        priority: num(j.priority, PRIORITY_NAMES),
        maxRetries: j.maxRetries ?? 3,
        timeoutMinutes: j.timeoutMinutes ?? 60,
        allowConcurrent: !!j.allowConcurrent,
        tags: j.tags ?? '',
        isEnabled: j.isEnabled !== false,
        autoPauseEnabled: j.autoPauseEnabled !== false,
      };
      tab = 'settings';
      revealed = '';
      hooks = await api.get<Webhook[]>(`/api/webhooks?jobId=${id}`).catch(() => []);
    } catch (e) {
      notify.fail(e);
    }
  }

  function dismiss() {
    form = null;
  }

  function close() {
    dismiss();
    records.close();
  }

  const selectedProfile = $derived(
    form?.profile.startsWith('export:') ? profiles.find((p) => p.id === form!.profile.split(':')[1]) : null,
  );
  const emailProfile = $derived(!!selectedProfile?.isEmailExport);

  async function save(e: SubmitEvent) {
    e.preventDefault();
    if (!form) return;
    busy = 'save';
    const [kind, idText] = form.profile ? form.profile.split(':') : ['', ''];
    const t = form.scheduleType;
    const body = {
      ...(form.id ? { id: form.id } : {}),
      name: form.name,
      description: form.description,
      type: form.type,
      profileId: kind === 'export' ? idText : null,
      importProfileId: kind === 'import' ? idText : null,
      destinationId: !emailProfile && form.destinationId ? form.destinationId : null,
      scheduleType: t,
      cronExpression: t === 1 ? form.cronExpression || null : null,
      intervalMinutes: t === 2 ? Number(form.intervalMinutes) || null : null,
      startTime: t >= 3 ? form.startTime || null : null,
      weekDays: t === 4 ? [...form.weekDays].sort().join(',') || null : null,
      monthDay: t === 5 ? Number(form.monthDay) || null : null,
      priority: Number(form.priority),
      maxRetries: Number(form.maxRetries),
      timeoutMinutes: Number(form.timeoutMinutes),
      allowConcurrent: form.allowConcurrent,
      tags: form.tags,
      isEnabled: form.isEnabled,
      autoPauseEnabled: form.autoPauseEnabled,
    };
    try {
      if (form.id) await api.put(`/api/jobs/${form.id}`, body);
      else await api.post('/api/jobs', body);
      notify.success(`${form.name} saved`, { key: 'job-save' });
      close();
      await load();
    } catch (err) {
      notify.fail(err, 'job-save');
    } finally {
      busy = null;
    }
  }

  const OUTCOME_LABEL: Record<Exclude<JobOutcome, 'running'>, string> = {
    success: 'Completed',
    partial: 'Completed with errors',
    failed: 'Failed',
  };
  let outcomes = $state<Record<string, Exclude<JobOutcome, 'running'>>>({});

  const latestExecution = (id: string) => api.get<JobExecution>(`/api/jobs/${id}/executions/latest`).catch(() => null);

  async function outcomeOf(j: Job, execution: JobExecution): Promise<JobOutcome> {
    const outcome = jobOutcome(execution);
    if (outcome !== 'success' || !j.profileId) return outcome;
    let executionId: string | undefined;
    try {
      executionId = JSON.parse(execution.outputData ?? '{}').executionId;
    } catch {}
    const run = executionId ? await api.get<ExportExecution>(`/api/executions/${executionId}`).catch(() => null) : null;
    return jobOutcome(execution, run);
  }

  async function trigger(j: Job) {
    const key = `job-run-${j.id}`;
    busy = `run-${j.id}`;
    try {
      const before = (await latestExecution(j.id))?.id ?? '';
      await api.post(`/api/jobs/${j.id}/trigger`);
      let execution: JobExecution | null = null;
      let outcome: JobOutcome = 'running';
      for (let i = 0; i < 120 && outcome === 'running'; i++) {
        await new Promise((r) => setTimeout(r, 1000));
        execution = await latestExecution(j.id);
        if (execution && execution.id > before) outcome = await outcomeOf(j, execution);
      }
      if (outcome === 'running') notify.info(`${j.name} is still running`, { key });
      else {
        if (outcome === 'success') notify.success(`${j.name} completed`, { key });
        else if (outcome === 'partial')
          notify.warning(`${j.name} completed with errors`, {
            key,
            description: execution?.errorMessage ?? 'Open the execution for details.',
          });
        else notify.error(`${j.name} failed`, { key, description: execution?.errorMessage ?? undefined });
        outcomes[j.id] = outcome;
        setTimeout(() => delete outcomes[j.id], 4000);
      }
      await Promise.all([load(), loadMetrics()]);
    } catch (e) {
      notify.fail(e, key);
    } finally {
      busy = null;
    }
  }

  async function act(j: Job, path: string, message: string) {
    try {
      await api.post(`/api/jobs/${j.id}/${path}`);
      notify.success(message);
      await load();
    } catch (e) {
      notify.fail(e);
    }
  }

  async function remove(j: Job) {
    if (
      !(await confirm({
        title: `Delete ${j.name}?`,
        message: 'Deleted jobs cannot be restored. The execution history stays.',
        confirm: 'Delete job',
        danger: true,
      }))
    )
      return;
    try {
      await api.del(`/api/jobs/${j.id}`);
      notify.success(`${j.name} deleted`);
      if (form?.id === j.id) close();
      await load();
    } catch (e) {
      notify.fail(e);
    }
  }

  async function hookAction(action: 'add' | 'toggle' | 'regenerate' | 'delete', hook?: Webhook) {
    if (!form?.id || (action !== 'add' && !hook)) return;
    try {
      if (action === 'add') reveal((await api.post<Webhook>('/api/webhooks', { jobId: form.id })).url ?? '');
      if (action === 'toggle') await api.post(`/api/webhooks/${hook!.id}/${hook!.isActive ? 'disable' : 'enable'}`);
      if (action === 'regenerate') {
        if (
          !(await confirm({
            title: 'Create a new token?',
            message: 'The current webhook URL stops working.',
            confirm: 'Create new token',
            danger: true,
          }))
        )
          return;
        reveal((await api.post<Webhook>(`/api/webhooks/${hook!.id}/regenerate`)).url ?? '');
      }
      if (action === 'delete') {
        if (
          !(await confirm({
            title: 'Delete webhook?',
            message: 'Calls to this webhook URL fail after deletion.',
            confirm: 'Delete webhook',
            danger: true,
          }))
        )
          return;
        await api.del(`/api/webhooks/${hook!.id}`);
      }
      hooks = await api.get<Webhook[]>(`/api/webhooks?jobId=${form.id}`);
    } catch (e) {
      notify.fail(e);
    }
  }

  function reveal(url: string) {
    revealed = url;
    copyText(url);
  }
</script>

<Page crumbs={[{ label: 'Jobs' }]}>
  {#snippet actions()}
    <button type="button" class="btn primary" onclick={() => records.open('new')}><Plus size={15} />New job</button>
  {/snippet}
  <div class="page-body">
    {#if metrics}
      <div class="queue" aria-label="Queue">
        <span><Activity size={14} /> Queue</span>
        <span
          >Active <span class="num">{formatNumber(metrics.activeJobs)}</span> of
          <span class="num">{formatNumber(metrics.maxConcurrentJobs)}</span></span
        >
        <span>Waiting <span class="num">{formatNumber(metrics.queueDepth)}</span></span>
        <span>Free slots <span class="num">{formatNumber(metrics.availableSlots)}</span></span>
        <span>Processed <span class="num">{formatNumber(metrics.totalDequeued)}</span></span>
      </div>
    {/if}

    <div class="toolbar">
      <input type="search" placeholder="Search job or profile" aria-label="Search jobs" bind:value={filters.search} />
      <select aria-label="Health" bind:value={filters.health}>
        <option value="">All health</option><option value="auto-paused">Auto-paused</option><option value="failing">Failing</option>
        <option value="warning">Warning</option><option value="healthy">Healthy</option><option value="disabled">Off</option>
      </select>
      <select aria-label="Status" bind:value={filters.status}>
        <option value="">All statuses</option>
        {#each Object.entries(STATUS) as [value, [label]] (value)}<option {value}>{label}</option>{/each}
      </select>
      <select aria-label="Profile" bind:value={filters.profile}>
        <option value="">All profiles</option>
        {#each profiles as p (p.id)}<option value={String(p.id)}>{p.name}</option>{/each}
      </select>
      <select aria-label="Connection" bind:value={filters.connection}>
        <option value="">All connections</option>
        {#each connections as c (c.id)}<option value={String(c.id)}>{c.name}</option>{/each}
      </select>
      <select aria-label="Group" bind:value={filters.group}>
        <option value="">All groups</option>
        {#each groups as g (g.id)}<option value={String(g.id)}>{g.name}</option>{/each}
      </select>
      {#if Object.values(filters).some(Boolean)}<button
          type="button"
          class="link"
          onclick={() => (filters = { search: '', health: '', status: '', profile: '', connection: '', group: '' })}>Clear filters</button
        >{/if}
    </div>

    <div class="table-wrap">
      <table class="data">
        <thead
          ><tr
            ><th>Name</th><th>Profile</th><th>Schedule</th><th>Status</th><th>Health</th><th>Last run</th><th>Next run</th><th
              ><span class="sr">Actions</span></th
            ></tr
          ></thead
        >
        <tbody>
          {#each visible as j (j.id)}
            {@const h = health(j)}
            {@const s = STATUS[num(j.status, STATUS_NAMES)] ?? ['Unknown', 'idle']}
            {@const linked = profileOf(j)}
            <tr
              class="clickable"
              onclick={(e) => {
                if (!(e.target as Element).closest('button, a')) records.open(j.id);
              }}
            >
              <td
                ><span class="strong">{j.name}</span>{#if j.description}<span class="sub">{j.description}</span>{/if}</td
              >
              <td
                >{linked?.name ?? j.profileName ?? ''}{#if j.importProfileId}
                  <span class="muted">Import</span>{/if}</td
              >
              <td>{schedule(j)}</td>
              <td><StatusMark kind={s[1]} label={s[0]} /></td>
              <td><span use:tooltip={h.hint}><StatusMark kind={h.kind} label={h.label} /></span></td>
              <td><span use:tooltip={formatDateTime(j.lastRunTime)}>{j.lastRunTime ? formatRelative(j.lastRunTime) : 'Never'}</span></td>
              <td><span use:tooltip={formatDateTime(j.nextRunTime)}>{j.nextRunTime ? formatRelative(j.nextRunTime) : ''}</span></td>
              <td class="actions">
                <button
                  type="button"
                  class="btn ghost icon run"
                  class:ok={outcomes[j.id] === 'success'}
                  class:warn={outcomes[j.id] === 'partial'}
                  class:err={outcomes[j.id] === 'failed'}
                  aria-label={outcomes[j.id] ? `${j.name}: ${OUTCOME_LABEL[outcomes[j.id]]}` : `Run ${j.name}`}
                  aria-busy={busy === `run-${j.id}`}
                  use:tooltip={outcomes[j.id] ? OUTCOME_LABEL[outcomes[j.id]] : 'Run now'}
                  onclick={() => trigger(j)}
                  >{#if outcomes[j.id] === 'success'}<CircleCheck size={15} />{:else if outcomes[j.id] === 'partial'}<TriangleAlert
                      size={15}
                    />{:else if outcomes[j.id] === 'failed'}<X size={15} />{:else}<Play size={15} />{/if}</button
                >
                {#if isPaused(j)}<button
                    type="button"
                    class="btn ghost icon"
                    aria-label="Resume {j.name}"
                    use:tooltip={'Resume'}
                    onclick={() => act(j, 'resume', `${j.name} resumed`)}><RotateCcw size={15} /></button
                  >{/if}
                {#if (j.consecutiveFailures || 0) > 0}<button
                    type="button"
                    class="btn ghost icon"
                    aria-label="Acknowledge failures of {j.name}"
                    use:tooltip={'Acknowledge failures'}
                    onclick={() => act(j, 'acknowledge-failures', 'Failures acknowledged')}><CircleCheck size={15} /></button
                  >{/if}
                <button
                  type="button"
                  class="btn ghost icon"
                  aria-label="History of {j.name}"
                  use:tooltip={'Execution history'}
                  onclick={() => navigate(`/executions?job=${j.id}`)}><History size={15} /></button
                >
                <button
                  type="button"
                  class="btn ghost icon"
                  aria-label="Edit {j.name}"
                  use:tooltip={'Edit'}
                  onclick={() => records.open(j.id)}><Pencil size={15} /></button
                >
                <button
                  type="button"
                  class="btn ghost icon danger"
                  aria-label="Delete {j.name}"
                  use:tooltip={'Delete'}
                  onclick={() => remove(j)}><Trash2 size={15} /></button
                >
              </td>
            </tr>
          {:else}
            <tr
              ><td colspan="8" class="empty">
                {#if !loaded}Loading jobs
                {:else if jobs.length === 0}
                  <div class="empty-state">
                    <h2>No jobs</h2>
                    <p>A job runs a profile on a schedule, an interval or a webhook call.</p>
                    <button type="button" class="btn primary" onclick={() => records.open('new')}>New job</button>
                  </div>
                {:else}No job matches the filters.{/if}
              </td></tr
            >
          {/each}
        </tbody>
      </table>
    </div>
  </div>
</Page>

{#if form}
  <Sheet title={form.id ? form.name || 'Job' : 'New job'} onclose={close}>
    {#if form.id}
      <div class="tabs" role="tablist">
        <button type="button" role="tab" aria-selected={tab === 'settings'} onclick={() => (tab = 'settings')}>Settings</button>
        <button type="button" role="tab" aria-selected={tab === 'webhooks'} onclick={() => (tab = 'webhooks')}
          >Webhooks <span class="num muted">{hooks.length}</span></button
        >
      </div>
    {/if}
    {#if tab === 'settings'}
      <form id="job-form" class="fields" onsubmit={save}>
        <div class="field"><label for="j-name">Name</label><input id="j-name" bind:value={form.name} autocomplete="off" /></div>
        <div class="field">
          <label for="j-desc">Description</label><textarea id="j-desc" rows="2" bind:value={form.description}></textarea>
        </div>
        <div class="field">
          <label for="j-profile">Profile</label>
          <select id="j-profile" bind:value={form.profile}>
            <option value="">Choose</option>
            <optgroup label="Export profiles"
              >{#each profiles as p (p.id)}<option value="export:{p.id}">{p.name}</option>{/each}</optgroup
            >
            {#if importProfiles.length}<optgroup label="Import profiles"
                >{#each importProfiles as p (p.id)}<option value="import:{p.id}">{p.name}</option>{/each}</optgroup
              >{/if}
          </select>
        </div>
        <div class="field">
          <label
            for="j-dest"
            use:tooltip={emailProfile ? 'Email profiles send through their email destination' : 'Overrides the destination of the profile'}
            >Destination</label
          >
          <select id="j-dest" bind:value={form.destinationId} disabled={emailProfile}>
            <option value="">Profile destination</option>
            {#each destinations.filter((d) => d.type !== 'Email') as d (d.id)}<option value={String(d.id)}>{d.name}</option>{/each}
          </select>
        </div>
        <div class="field">
          <label for="j-sched">Schedule</label>
          <select id="j-sched" bind:value={form.scheduleType}
            >{#each SCHEDULES as label, i (i)}<option value={i}>{label}</option>{/each}</select
          >
        </div>
        {#if form.scheduleType === 1}
          <div class="field">
            <label for="j-cron" use:tooltip={'0 2 * * * runs daily at 02:00'}>Cron expression</label><input
              id="j-cron"
              class="number"
              placeholder="0 2 * * *"
              bind:value={form.cronExpression}
            />
          </div>
        {:else if form.scheduleType === 2}
          <div class="field">
            <label for="j-int">Interval in minutes</label><input id="j-int" type="number" min="1" bind:value={form.intervalMinutes} />
          </div>
        {:else if form.scheduleType >= 3}
          {#if form.scheduleType === 4}
            <fieldset class="days">
              <legend>Days</legend>{#each DAYS as day, i (day)}<label class="check"
                  ><input
                    type="checkbox"
                    checked={form.weekDays.includes(i)}
                    onchange={(e) => {
                      form!.weekDays = e.currentTarget.checked ? [...form!.weekDays, i] : form!.weekDays.filter((d) => d !== i);
                    }}
                  />{day}</label
                >{/each}
            </fieldset>
          {/if}
          {#if form.scheduleType === 5}
            <div class="field">
              <label for="j-mday">Day of the month</label><input id="j-mday" type="number" min="1" max="31" bind:value={form.monthDay} />
            </div>
          {/if}
          <div class="field">
            <label for="j-time" use:tooltip={'Server time'}>Start time</label><input
              id="j-time"
              type="time"
              class="number"
              bind:value={form.startTime}
            />
          </div>
        {/if}
        <div class="grid2">
          <div class="field">
            <label for="j-prio">Priority</label><select id="j-prio" bind:value={form.priority}
              >{#each PRIORITIES as label, i (i)}<option value={i}>{label}</option>{/each}</select
            >
          </div>
          <div class="field">
            <label for="j-retry">Retries</label><input id="j-retry" type="number" min="0" max="10" bind:value={form.maxRetries} />
          </div>
        </div>
        <div class="field">
          <label for="j-timeout">Timeout in minutes</label><input
            id="j-timeout"
            type="number"
            min="1"
            max="1440"
            bind:value={form.timeoutMinutes}
          />
        </div>
        <div class="field">
          <label for="j-tags">Tags</label><input id="j-tags" bind:value={form.tags} use:tooltip={'Comma-separated'} />
        </div>
        <label class="check"><input type="checkbox" bind:checked={form.allowConcurrent} />Allow concurrent runs</label>
        <label class="check"><input type="checkbox" role="switch" bind:checked={form.isEnabled} />Enabled</label>
        <label class="check" use:tooltip={'Pause the job after 10 consecutive failures'}
          ><input type="checkbox" bind:checked={form.autoPauseEnabled} />Auto-pause on repeated failures</label
        >
      </form>
    {:else}
      <div class="fields">
        <p class="muted">A webhook runs this job on an HTTP POST to its URL.</p>
        {#each hooks as hook (hook.id)}
          <div class="hook">
            <span class="num token">{hook.token}</span>
            <span class="muted">{hook.isActive ? 'On' : 'Off'}, <span class="num">{hook.triggerCount ?? 0}</span> calls</span>
            <span class="links">
              <button type="button" class="link" onclick={() => copyText(`curl -X POST ${location.origin}/webhooks/${hook.token}`)}
                ><Copy size={12} /> curl</button
              >
              <button type="button" class="link" onclick={() => hookAction('toggle', hook)}>{hook.isActive ? 'Turn off' : 'Turn on'}</button
              >
              <button type="button" class="link" onclick={() => hookAction('regenerate', hook)}>New token</button>
              <button type="button" class="link danger" onclick={() => hookAction('delete', hook)}>Delete</button>
            </span>
          </div>
        {:else}
          <p class="muted">No webhooks</p>
        {/each}
        <div><button type="button" class="btn" onclick={() => hookAction('add')}><Plus size={14} />Add webhook</button></div>
        {#if revealed}
          <div class="field">
            <label for="j-hook">Webhook URL. The token is displayed once.</label><input
              id="j-hook"
              readonly
              value={revealed}
              onfocus={(e) => e.currentTarget.select()}
            />
          </div>
        {/if}
      </div>
    {/if}
    {#snippet footer()}
      {#if tab === 'settings'}
        <button type="button" class="btn" onclick={close}>Cancel</button>
        <button type="submit" form="job-form" class="btn primary" aria-busy={busy === 'save'}>Save</button>
      {:else}
        <button type="button" class="btn" onclick={close}>Close</button>
      {/if}
    {/snippet}
  </Sheet>
{/if}

<style>
  .run.ok {
    color: var(--ok);
  }
  .run.warn {
    color: var(--warn);
  }
  .run.err {
    color: var(--err);
  }
  .strong {
    font-weight: 500;
    display: block;
  }
  .sub {
    display: block;
    font-size: 12px;
    color: var(--muted);
    max-width: 320px;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  .queue {
    display: flex;
    flex-wrap: wrap;
    gap: 8px 24px;
    align-items: center;
    padding: 10px 14px;
    background: var(--surface);
    border: 1px solid var(--line);
    border-radius: var(--radius);
    color: var(--soft);
  }
  .queue span:first-child {
    display: inline-flex;
    align-items: center;
    gap: 6px;
    font-weight: 500;
    color: var(--ink);
  }
  .days {
    border: 1px solid var(--line);
    border-radius: var(--radius);
    padding: 10px 12px;
    margin: 0;
    display: grid;
    grid-template-columns: repeat(2, minmax(0, 1fr));
    gap: 8px;
  }
  .days legend {
    font-size: 13px;
    font-weight: 500;
    color: var(--soft);
    padding: 0 4px;
  }
  .hook {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 6px 12px;
    padding: 8px 0;
    border-bottom: 1px solid var(--line);
  }
  .token {
    flex: 1;
    min-width: 0;
    overflow: hidden;
    text-overflow: ellipsis;
  }
  .links {
    display: flex;
    gap: 10px;
  }
  .links button {
    display: inline-flex;
    align-items: center;
    gap: 3px;
  }
  .danger {
    color: var(--err);
  }
</style>
