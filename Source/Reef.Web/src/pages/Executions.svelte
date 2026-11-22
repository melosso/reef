<script lang="ts">
  import { Pencil, Play, RefreshCw } from '@lucide/svelte';
  import Page from '../components/Page.svelte';
  import Pager from '../components/Pager.svelte';
  import Sheet from '../components/Sheet.svelte';
  import StatusMark from '../components/StatusMark.svelte';
  import { api } from '../lib/api';
  import { formatDateTime, formatDuration, formatRelative } from '../lib/format';
  import { notify } from '../lib/notify';
  import { urlPaging } from '../lib/paging.svelte';
  import { queryString } from '../lib/query';
  import { match, navigate, route, setQuery } from '../lib/router.svelte';
  import { statusKind, statusLabel } from '../lib/status';
  import { tooltip } from '../lib/tooltip';
  import { runWorkflow } from '../lib/workflows';
  import type { PagedResult } from '../models/PagedResult';
  import type { Workflow } from '../models/Workflow';
  import type { WorkflowCatalog } from '../models/WorkflowCatalog';
  import type { WorkflowExecution } from '../models/WorkflowExecution';

  const paging = urlPaging(25);
  const detailId = $derived(match('/executions/:id', route.path)?.id ?? null);
  const workflowFilter = $derived(route.query.get('workflow') ?? '');
  const statusFilter = $derived(route.query.get('status') ?? '');

  let workflows = $state<Workflow[]>([]);
  let labels = $state<Record<string, string>>({});
  let runs = $state<WorkflowExecution[]>([]);
  let total = $state(0);
  let pages = $state(1);
  let loaded = $state(false);
  let detail = $state<WorkflowExecution | null>(null);
  let rerunning = $state(false);

  const detailWorkflow = $derived(detail ? workflows.find((w) => w.id === detail!.workflowId) : undefined);

  async function load() {
    try {
      const q = queryString({ page: paging.page, pageSize: paging.size, workflowId: workflowFilter, status: statusFilter });
      const result = await api.get<PagedResult<WorkflowExecution>>(`/api/workflow-executions?${q}`);
      runs = result.data;
      total = result.totalCount;
      pages = Math.max(1, result.totalPages);
    } catch (e) {
      notify.fail(e, 'executions');
    }
    loaded = true;
  }

  $effect(() => {
    api
      .get<Workflow[]>('/api/workflows')
      .then((list) => (workflows = list))
      .catch(() => []);
  });

  $effect(() => {
    api
      .get<WorkflowCatalog>('/api/workflows/catalog')
      .then((c) => (labels = Object.fromEntries(c.nodes.map((d) => [d.type, d.label]))))
      .catch(() => {});
  });

  $effect(() => {
    void paging.page;
    void paging.size;
    void workflowFilter;
    void statusFilter;
    load();
  });

  $effect(() => {
    if (!runs.some((r) => r.status === 'Running')) return;
    const timer = setInterval(load, 5000);
    return () => clearInterval(timer);
  });

  $effect(() => {
    const id = detailId;
    detail = null;
    if (id)
      api
        .get<WorkflowExecution>(`/api/workflow-executions/${id}`)
        .then((r) => (detail = r))
        .catch((e) => notify.fail(e, 'execution'));
  });

  function filter(key: string, value: string) {
    setQuery('page', null);
    setQuery(key, value || null);
  }

  function duration(run: WorkflowExecution): string {
    return run.completedAt ? formatDuration(Date.parse(run.completedAt) - Date.parse(run.startedAt)) : '';
  }

  function stepName(nodeId: string, workflow = detailWorkflow): string {
    const node = workflow?.nodes.find((n) => n.id === nodeId);
    return node?.name || (node ? (labels[node.type] ?? nodeId) : nodeId);
  }

  async function rerun() {
    if (!detailWorkflow) return;
    rerunning = true;
    try {
      const started = await runWorkflow(detailWorkflow);
      if (started) navigate(`/executions/${started.id}`);
      await load();
    } catch (e) {
      notify.fail(e, 'rerun');
    } finally {
      rerunning = false;
    }
  }
</script>

<Page crumbs={[{ label: 'Executions' }]}>
  {#snippet actions()}
    <button type="button" class="btn ghost icon" aria-label="Refresh" use:tooltip={'Refresh'} onclick={load}>
      <RefreshCw size={15} />
    </button>
  {/snippet}
  <div class="page-body">
    <div class="toolbar">
      <select aria-label="Workflow" value={workflowFilter} onchange={(e) => filter('workflow', e.currentTarget.value)}>
        <option value="">All workflows</option>
        {#each workflows as w (w.id)}<option value={w.id}>{w.name}</option>{/each}
      </select>
      <select aria-label="Status" value={statusFilter} onchange={(e) => filter('status', e.currentTarget.value)}>
        <option value="">All statuses</option>
        <option value="Success">Success</option>
        <option value="Failed">Failed</option>
        <option value="Running">Running</option>
      </select>
    </div>
    <div class="table-wrap">
      <table class="data">
        <thead>
          <tr><th>Workflow</th><th>Trigger</th><th>Status</th><th class="secondary">Reason</th><th>Started</th><th>Duration</th></tr>
        </thead>
        <tbody>
          {#each runs as run (run.id)}
            <tr class="clickable" onclick={() => navigate(`/executions/${run.id}${location.search}`)}>
              <td class="strong">{run.workflowName ?? 'Deleted workflow'}</td>
              <td>
                {stepName(
                  run.triggerNodeId,
                  workflows.find((w) => w.id === run.workflowId),
                )}
              </td>
              <td>
                <span use:tooltip={run.errorMessage}><StatusMark kind={statusKind(run.status)} label={statusLabel(run.status)} /></span>
              </td>
              <td class="muted trunc secondary">
                {run.errorMessage?.split('\n')[0] ?? (run.status === 'Failed' ? 'No error message recorded' : '')}
              </td>
              <td><span use:tooltip={formatDateTime(run.startedAt)}>{formatRelative(run.startedAt)}</span></td>
              <td class="num">{duration(run)}</td>
            </tr>
          {:else}
            <tr>
              <td colspan="6" class="empty">
                {#if !loaded}Loading runs
                {:else if workflowFilter || statusFilter}No run matches the filters.
                {:else}
                  <div class="empty-state">
                    <h2>No runs</h2>
                    <p>Workflow runs from the editor, schedules and webhooks are listed here.</p>
                    <button type="button" class="btn primary" onclick={() => navigate('/workflows')}>Open workflows</button>
                  </div>
                {/if}
              </td>
            </tr>
          {/each}
        </tbody>
      </table>
    </div>
    <Pager bind:page={paging.page} bind:pageSize={paging.size} {total} {pages} />
  </div>
</Page>

{#if detailId}
  <Sheet title={detail?.workflowName ?? 'Run'} onclose={() => navigate(`/executions${location.search}`)}>
    {#if detail}
      <dl class="facts">
        <dt>Status</dt>
        <dd><StatusMark kind={statusKind(detail.status)} label={statusLabel(detail.status)} /></dd>
        <dt>Trigger</dt>
        <dd>{stepName(detail.triggerNodeId)}</dd>
        <dt>Started</dt>
        <dd>{formatDateTime(detail.startedAt)}</dd>
        <dt>Duration</dt>
        <dd class="num">{duration(detail)}</dd>
      </dl>
      {#if detail.errorMessage}<pre class="error">{detail.errorMessage}</pre>{/if}
      <div class="table-wrap">
        <table class="data">
          <thead><tr><th>Step</th><th>Status</th><th>Rows out</th><th>Pass</th></tr></thead>
          <tbody>
            {#each detail.nodes as step (step.id)}
              <tr>
                <td>{stepName(step.nodeId)}</td>
                <td>
                  <span use:tooltip={step.errorMessage}>
                    <StatusMark kind={statusKind(step.status)} label={statusLabel(step.status)} />
                  </span>
                </td>
                <td class="num">{step.rowCount}</td>
                <td class="num">{step.iteration + 1}</td>
              </tr>
            {/each}
          </tbody>
        </table>
      </div>
    {:else}
      <p class="muted">Loading run</p>
    {/if}
    {#snippet footer()}
      <button
        type="button"
        class="btn"
        disabled={!detailWorkflow}
        onclick={() => navigate(`/workflows/${detail!.workflowId}?run=${detail!.id}`)}
      >
        <Pencil size={14} />Open in editor
      </button>
      <button type="button" class="btn primary" aria-busy={rerunning} disabled={!detailWorkflow || rerunning} onclick={rerun}>
        <Play size={14} />Run again
      </button>
    {/snippet}
  </Sheet>
{/if}

<style>
  .facts {
    display: grid;
    grid-template-columns: max-content 1fr;
    gap: 8px 16px;
    margin: 0 0 16px;
  }
  .facts dt {
    color: var(--muted);
  }
  .facts dd {
    margin: 0;
  }
  .error {
    margin: 0 0 16px;
    padding: 10px 12px;
    border: 1px solid var(--err);
    border-radius: var(--radius);
    font-family: var(--font);
    font-size: 13px;
    white-space: pre-wrap;
    color: var(--err);
  }
</style>
