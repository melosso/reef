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
  const upNextGroups = $derived.by(() => {
    const groups: Record<string, (typeof upNext)[number][]> = {};
    for (const item of upNext) (groups[item.at.slice(0, 16)] ??= []).push(item);
    return Object.values(groups)
      .slice(0, 6)
      .map((items) => ({ ...items[0], more: items.length - 1 }));
  });
  const issues = $derived((attention?.failed.length ?? 0) + (attention?.anomalies.length ?? 0) + (approvals ? 1 : 0));
</script>

<Page crumbs={[{ label: 'Dashboard' }]}>
  <div class="page-body">
    {#if attention}
      <section class="panel">
        <div class="panel-head">
          <h2>Needs attention</h2>
          <span class="spacer"></span>
          <span class="muted">
            Last 24 hours: {formatNumber(attention.last24h.runs)}
            {attention.last24h.runs === 1 ? 'run' : 'runs'}{#if attention.last24h.failed},
              <a class="failed" href="/executions?status=Failed">{formatNumber(attention.last24h.failed)} failed</a>{/if}
          </span>
        </div>
        {#if issues}
          <table class="data">
            <tbody>
              {#each attention.failed as f (f.executionId)}
                <tr class="clickable" onclick={() => navigate(`/executions/${f.executionId}`)}>
                  <td class="mark"><a class="plain" href="/executions/{f.executionId}"><StatusMark kind="err" label="Failed" /></a></td>
                  <td class="name">
                    <a
                      class="strong"
                      href="/workflows/{f.workflowId}?run={f.executionId}"
                      onclick={(e) => open(e, `/workflows/${f.workflowId}?run=${f.executionId}`)}
                    >
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
                  <td class="name">
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
          <div class="panel-body calm">
            {#if attention.last24h.failed}
              <StatusMark kind="ok" label="No workflow is failing now." />
              <a class="failed" href="/executions?status=Failed">
                {formatNumber(attention.last24h.failed)} earlier runs failed in the last 24 hours
              </a>
            {:else}<StatusMark kind="ok" label="Every workflow's latest run succeeded." />{/if}
          </div>
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
              {#each upNextGroups as item (item.key)}
                <tr class="clickable" onclick={() => navigate(item.more ? '/workflows' : item.href)}>
                  <td>
                    <a class="strong" href={item.more ? '/workflows' : item.href}>{item.name}</a>{#if item.more}<span class="muted">
                        {` and ${formatNumber(item.more)} more`}
                      </span>{/if}
                  </td>
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
                  <td class="mark">
                    <span use:tooltip={run.errorMessage}><StatusMark kind={statusKind(run.status)} label={statusLabel(run.status)} /></span>
                  </td>
                  <td><a class="strong" href="/executions/{run.id}">{run.workflowName ?? 'Deleted workflow'}</a></td>
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
  .calm {
    display: flex;
    flex-wrap: wrap;
    gap: 6px 16px;
  }
  .failed {
    color: var(--err);
  }
  .spacer {
    flex: 1;
  }
  .plain {
    color: inherit;
    text-decoration: none;
  }
  .strong {
    font-weight: 500;
    color: var(--ink);
    text-decoration: none;
  }
  td.name {
    white-space: nowrap;
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
