<script lang="ts">
  import Page from '../components/Page.svelte';
  import StatusMark from '../components/StatusMark.svelte';
  import { api } from '../lib/api';
  import { formatDateTime, formatDuration, formatNumber, formatRelative, parseDate } from '../lib/format';
  import { notify } from '../lib/notify';
  import { navigate } from '../lib/router.svelte';
  import { statusKind, statusLabel } from '../lib/status';
  import { tooltip } from '../lib/tooltip';

  const CONNECTION_TYPES: Record<string, string> = { SqlServer: 'SQL Server', MySQL: 'MySQL', PostgreSQL: 'PostgreSQL' };

  interface Run {
    id: number;
    kind: 'export' | 'import';
    name: string;
    code?: string;
    status: string;
    startedAt: string;
    durationMs?: number | null;
    href: string;
  }

  let stats = $state<any>(null);
  let connections = $state<any[]>([]);
  let runs = $state<Run[]>([]);
  let period = $state<'week' | 'today'>('week');

  async function load() {
    try {
      const [metrics, executions, conns, imports] = await Promise.all([
        api.get('/api/admin/metrics').catch(() => ({})),
        api.get('/api/executions?pageSize=10').catch(() => ({ data: [] })),
        api.get<any[]>('/api/connections').catch(() => []),
        api.get<any[]>('/api/profiles?type=import').catch(() => []),
      ]);
      stats = metrics ?? {};
      connections = (conns ?? []).filter((c) => c.isActive);
      const exportRuns: Run[] = ((executions?.data ?? executions) || []).map((e: any) => ({
        id: e.id,
        kind: 'export',
        name: e.profileName || 'Unknown profile',
        code: e.profileCode,
        status: e.status,
        startedAt: e.startedAt,
        durationMs: e.executionTimeMs,
        href: `/executions?id=${e.id}`,
      }));
      const importLists = await Promise.all(
        (imports ?? []).map((p) =>
          api
            .get<any[]>(`/api/profiles/${p.id}/executions?type=import&limit=3`)
            .then((list) =>
              (list ?? []).map((e: any): Run => ({
                id: e.id,
                kind: 'import',
                name: p.name,
                code: p.code,
                status: e.status,
                startedAt: e.startedAt,
                durationMs: e.completedAt ? parseDate(e.completedAt)!.getTime() - parseDate(e.startedAt)!.getTime() : null,
                href: `/executions?importId=${e.id}`,
              })),
            )
            .catch(() => [] as Run[]),
        ),
      );
      const time = (r: Run) => parseDate(r.startedAt)?.getTime() ?? 0;
      runs = [
        ...exportRuns,
        ...importLists
          .flat()
          .sort((a, b) => time(b) - time(a))
          .slice(0, 5),
      ]
        .sort((a, b) => time(b) - time(a))
        .slice(0, 10);
    } catch (e) {
      notify.fail(e, 'dashboard');
    }
  }

  $effect(() => {
    load();
  });

  const todayCount = $derived.by(() => {
    const start = new Date();
    start.setHours(0, 0, 0, 0);
    return runs.filter((r) => (parseDate(r.startedAt)?.getTime() ?? 0) >= start.getTime()).length;
  });
  const dbPercent = $derived(
    stats?.system?.databaseSizeMb !== undefined
      ? Math.round((stats.system.databaseSizeMb / (stats.system.databaseSizeThresholdMb || 1024)) * 100)
      : null,
  );
</script>

<Page crumbs={[{ label: 'Dashboard' }]}>
  <div class="page-body">
    <div class="cards">
      <a class="stat" href="/profiles"
        ><span class="label">Profiles</span><span class="value">{formatNumber(stats?.profiles?.total) || '0'}</span></a
      >
      <a class="stat" href="/connections"
        ><span class="label">Connections</span><span class="value">{formatNumber(stats?.connections) || '0'}</span></a
      >
      <button
        type="button"
        class="stat"
        use:tooltip={period === 'week' ? 'Show executions today' : 'Show executions in the last 7 days'}
        onclick={() => (period = period === 'week' ? 'today' : 'week')}
      >
        <span class="label">{period === 'week' ? 'Executions, last 7 days' : 'Executions today'}</span>
        <span class="value">{formatNumber(period === 'week' ? (stats?.recentStats?.last7Days?.total ?? 0) : todayCount)}</span>
      </button>
      <div class="stat"><span class="label">Users</span><span class="value">{formatNumber(stats?.users) || '0'}</span></div>
      <div class="stat"><span class="label">API keys</span><span class="value">{formatNumber(stats?.apikeys) || '0'}</span></div>
      <div class="stat"><span class="label">Export storage</span><span class="value">{stats?.system?.exportsSizeMb ?? 0} MB</span></div>
      <div class="stat">
        <span class="label">Database size</span>
        <span class="value" class:warn={(dbPercent ?? 0) >= 80} class:err={(dbPercent ?? 0) >= 100}
          >{stats?.system?.databaseSizeMb ?? 0} MB</span
        >
        {#if dbPercent !== null}
          <span
            class="meter"
            role="meter"
            aria-valuenow={dbPercent}
            aria-valuemin="0"
            aria-valuemax="100"
            aria-label="Database size of threshold"
            use:tooltip={`Threshold ${stats.system.databaseSizeThresholdMb || 1024} MB`}
          >
            <span style:width="{Math.min(dbPercent, 100)}%"></span>
          </span>
        {/if}
      </div>
      <div class="stat">
        <span class="label">Notifications</span>
        <StatusMark kind={stats?.notifications?.isEnabled ? 'ok' : 'off'} label={stats?.notifications?.isEnabled ? 'On' : 'Off'} />
      </div>
    </div>

    <div class="split">
      <section class="panel">
        <div class="panel-head">
          <h2>Recent executions</h2>
          <span class="spacer"></span><a href="/executions">View all</a>
        </div>
        <table class="data">
          <thead><tr><th>Profile</th><th>Kind</th><th>Status</th><th>Started</th><th>Duration</th></tr></thead>
          <tbody>
            {#each runs as run (run.kind + run.id)}
              <tr class="clickable" onclick={() => navigate(run.href)}>
                <td
                  ><a href={run.href} class="strong">{run.name}</a>{#if run.code}<span class="num muted code-ref">{run.code}</span>{/if}</td
                >
                <td>{run.kind === 'import' ? 'Import' : 'Export'}</td>
                <td><StatusMark kind={statusKind(run.status)} label={statusLabel(run.status)} /></td>
                <td><span use:tooltip={formatDateTime(run.startedAt)}>{formatRelative(run.startedAt)}</span></td>
                <td class="num">{formatDuration(run.durationMs)}</td>
              </tr>
            {:else}
              <tr><td colspan="5" class="empty">No executions</td></tr>
            {/each}
          </tbody>
        </table>
      </section>
      <section class="panel">
        <div class="panel-head">
          <h2>Active connections</h2>
          <span class="spacer"></span><a href="/connections">View all</a>
        </div>
        <table class="data">
          <thead><tr><th>Name</th><th>Type</th><th>Last tested</th></tr></thead>
          <tbody>
            {#each connections as conn (conn.id)}
              <tr class="clickable" onclick={() => navigate(`/connections?id=${conn.id}`)}>
                <td class="strong">{conn.name}</td>
                <td>{CONNECTION_TYPES[conn.type] ?? conn.type}</td>
                <td>{conn.lastTestedAt ? formatRelative(conn.lastTestedAt) : 'Never'}</td>
              </tr>
            {:else}
              <tr><td colspan="3" class="empty">No active connections</td></tr>
            {/each}
          </tbody>
        </table>
      </section>
    </div>
  </div>
</Page>

<style>
  a.stat,
  button.stat {
    color: inherit;
    text-decoration: none;
    text-align: left;
    font: inherit;
    cursor: pointer;
  }
  a.stat:hover,
  button.stat:hover {
    border-color: var(--line-strong);
  }
  .value.warn {
    color: var(--warn);
  }
  .value.err {
    color: var(--err);
  }
  .meter {
    display: block;
    height: 4px;
    background: var(--hover);
    border-radius: 2px;
    overflow: hidden;
  }
  .meter span {
    display: block;
    height: 100%;
    background: var(--accent);
  }
  .split {
    display: grid;
    grid-template-columns: minmax(0, 2fr) minmax(0, 1fr);
    gap: 16px;
    align-items: start;
  }
  @media (max-width: 1200px) {
    .split {
      grid-template-columns: minmax(0, 1fr);
    }
  }
  .spacer {
    flex: 1;
  }
  .strong {
    font-weight: 500;
    color: var(--ink);
    text-decoration: none;
  }
</style>
