<script lang="ts">
  import Page from '../components/Page.svelte';
  import StatusMark from '../components/StatusMark.svelte';
  import { api } from '../lib/api';
  import { formatDateTime, formatDuration, formatNumber, formatRelative } from '../lib/format';
  import { notify } from '../lib/notify';
  import { navigate } from '../lib/router.svelte';
  import { tooltip } from '../lib/tooltip';
  import type { AttentionAnomaly } from '../models/AttentionAnomaly';
  import type { DashboardAttention } from '../models/DashboardAttention';
  import type { PagedResult } from '../models/PagedResult';
  import type { WorkflowExecution } from '../models/WorkflowExecution';
  import { statusKind, statusLabel } from '../lib/status';

  let attention = $state<DashboardAttention | null>(null);
  let approvals = $state(0);
  let recentRuns = $state<WorkflowExecution[]>([]);

  async function load() {
    try {
      const [data, pending, runs] = await Promise.all([
        api.get<DashboardAttention>('/api/dashboard/attention'),
        api.get<{ count: number }>('/api/email-approvals/count').catch(() => ({ count: 0 })),
        api.get<PagedResult<WorkflowExecution>>('/api/workflow-executions?pageSize=8').catch(() => null),
      ]);
      attention = data;
      recentRuns = runs?.data ?? [];
      approvals = pending?.count ?? 0;
    } catch (e) {
      notify.fail(e, 'dashboard');
    }
  }

  $effect(() => {
    load();
  });

  function open(e: MouseEvent, href: string) {
    e.preventDefault();
    e.stopPropagation();
    navigate(href);
  }

  const describe = (a: AttentionAnomaly) =>
    a.reason === 'rows'
      ? `${formatNumber(a.value)} rows, usually ${formatNumber(a.median)}`
      : `Took ${formatDuration(a.value)}, usually ${formatDuration(a.median)}`;
  const upNext = $derived(
    attention
      ? [
          ...attention.upNextWorkflows.map((w) => ({
            key: `w${w.workflowId}${w.nextRunTime}`,
            name: w.workflowName,
            at: w.nextRunTime,
            href: `/workflows/${w.workflowId}`,
          })),
          ...attention.upNext.map((j) => ({ key: `j${j.id}`, name: j.name, at: j.nextRunTime, href: `/jobs/${j.id}` })),
        ].sort((a, b) => Date.parse(a.at) - Date.parse(b.at))
      : [],
  );
  const issues = $derived((attention?.failed.length ?? 0) + (attention?.anomalies.length ?? 0) + (approvals ? 1 : 0));
</script>

<Page crumbs={[{ label: 'Dashboard' }]}>
  <div class="page-body">
    {#if attention}
      <section class="panel">
        <div class="panel-head">
          <h2>Needs attention</h2>
          <span class="spacer"></span>
          <span class="muted" use:tooltip={'Runs started in the last 24 hours'}>
            {formatNumber(attention.last24h.runs)}
            {attention.last24h.runs === 1 ? 'run' : 'runs'}, {formatNumber(attention.last24h.failed)} failed
          </span>
        </div>
        {#if issues}
          <table class="data">
            <tbody>
              {#each attention.failed as f (f.executionId)}
                <tr class="clickable" onclick={() => navigate(`/executions/${f.executionId}`)}>
                  <td class="mark"><StatusMark kind="err" label="Failed" /></td>
                  <td>
                    <a class="strong" href="/workflows/{f.workflowId}" onclick={(e) => open(e, `/workflows/${f.workflowId}`)}>
                      {f.workflowName}
                    </a>
                  </td>
                  <td class="detail" use:tooltip={f.error}>{f.error}</td>
                  <td class="when"><span use:tooltip={formatDateTime(f.startedAt)}>{formatRelative(f.startedAt)}</span></td>
                </tr>
              {/each}
              {#each attention.anomalies as a (a.executionId)}
                <tr class="clickable" onclick={() => navigate(`/executions/${a.executionId}`)}>
                  <td class="mark"><StatusMark kind="warn" label={a.reason === 'rows' ? 'Fewer rows' : 'Slower'} /></td>
                  <td>
                    <a class="strong" href="/workflows/{a.workflowId}" onclick={(e) => open(e, `/workflows/${a.workflowId}`)}>
                      {a.workflowName}
                    </a>
                  </td>
                  <td class="detail">{describe(a)}</td>
                  <td class="when"><span use:tooltip={formatDateTime(a.startedAt)}>{formatRelative(a.startedAt)}</span></td>
                </tr>
              {/each}
              {#if approvals}
                <tr class="clickable" onclick={() => navigate('/approvals')}>
                  <td class="mark"><StatusMark kind="idle" label="Waiting" /></td>
                  <td><a class="strong" href="/approvals">Email approvals</a></td>
                  <td class="detail">{formatNumber(approvals)} {approvals === 1 ? 'email waits' : 'emails wait'} for approval</td>
                  <td class="when"></td>
                </tr>
              {/if}
            </tbody>
          </table>
        {:else}
          <div class="panel-body"><StatusMark kind="ok" label="All clear. Every workflow's latest run succeeded." /></div>
        {/if}
      </section>

      <section class="panel">
        <div class="panel-head">
          <h2>Up next</h2>
          <span class="spacer"></span><a href="/workflows">All workflows</a>
        </div>
        {#if upNext.length}
          <table class="data">
            <tbody>
              {#each upNext as item (item.key)}
                <tr class="clickable" onclick={() => navigate(item.href)}>
                  <td class="strong">{item.name}</td>
                  <td class="when"><span use:tooltip={formatDateTime(item.at)}>{formatRelative(item.at)}</span></td>
                </tr>
              {/each}
            </tbody>
          </table>
        {:else}
          <div class="panel-body muted">Nothing scheduled in the next 24 hours.</div>
        {/if}
      </section>
      <section class="panel">
        <div class="panel-head">
          <h2>Recent runs</h2>
          <span class="spacer"></span><a href="/executions">All runs</a>
        </div>
        {#if recentRuns.length}
          <table class="data">
            <tbody>
              {#each recentRuns as run (run.id)}
                <tr class="clickable" onclick={() => navigate(`/executions/${run.id}`)}>
                  <td class="mark"><StatusMark kind={statusKind(run.status)} label={statusLabel(run.status)} /></td>
                  <td class="strong">{run.workflowName ?? 'Deleted workflow'}</td>
                  <td class="when"><span use:tooltip={formatDateTime(run.startedAt)}>{formatRelative(run.startedAt)}</span></td>
                </tr>
              {/each}
            </tbody>
          </table>
        {:else}
          <div class="panel-body muted">No runs yet.</div>
        {/if}
      </section>
    {/if}
  </div>
</Page>

<style>
  .spacer {
    flex: 1;
  }
  .strong {
    font-weight: 500;
    color: var(--ink);
    text-decoration: none;
  }
  td.mark {
    width: 1%;
    white-space: nowrap;
  }
  td.detail {
    color: var(--soft);
    max-width: 0;
    width: 60%;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  td.when {
    width: 1%;
    white-space: nowrap;
    text-align: right;
    color: var(--muted);
  }
</style>
