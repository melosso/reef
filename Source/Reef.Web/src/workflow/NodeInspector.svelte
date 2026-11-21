<script lang="ts">
  import { Copy, FlaskConical, Link, Play, Trash2, X } from '@lucide/svelte';
  import StatusMark from '../components/StatusMark.svelte';
  import { api } from '../lib/api';
  import { formatDateTime, formatDuration } from '../lib/format';
  import { copyText, notify } from '../lib/notify';
  import { tooltip } from '../lib/tooltip';
  import FieldControl from '../editor/FieldControl.svelte';
  import { visible } from '../editor/config';
  import { stepIcon } from '../editor/icons';
  import type { Options } from '../editor/types';
  import type { FieldError } from '../lib/api';
  import type { ItemsSample } from '../models/ItemsSample';
  import type { NodeTestResult } from '../models/NodeTestResult';
  import type { WebhookToken } from '../models/WebhookToken';
  import type { Workflow } from '../models/Workflow';
  import type { WorkflowExecution } from '../models/WorkflowExecution';
  import type { WorkflowNode } from '../models/WorkflowNode';
  import type { WorkflowNodeDefinition } from '../models/WorkflowNodeDefinition';
  import ItemsTable from './ItemsTable.svelte';
  import { lastPass, samples } from './graph';

  let {
    workflow,
    node,
    def,
    options,
    errors,
    run,
    dirty,
    onchange,
    onrename,
    onremove,
    onclose,
    onrun,
  }: {
    workflow: Workflow;
    node: WorkflowNode;
    def: WorkflowNodeDefinition;
    options: Options;
    errors: FieldError[];
    run: WorkflowExecution | null;
    dirty: boolean;
    onchange: (key: string) => void;
    onrename: (name: string) => void;
    onremove: () => void;
    onclose: () => void;
    onrun: () => void;
  } = $props();

  const FULL_RUN_ONLY = ['Deliver', 'Hooks'];
  let tab = $state<'settings' | 'input' | 'output' | 'run'>('settings');
  let test = $state<NodeTestResult | null>(null);
  let testing = $state(false);
  let hookUrl = $state<string | null>(null);

  const Icon = $derived(stepIcon(def.icon));
  const fields = $derived(def.fields.filter((f) => visible(f, node.config)));
  const passes = $derived(run ? run.nodes.filter((r) => r.nodeId === node.id) : []);
  const latest = $derived(passes.at(-1));
  const output = $derived(test?.outputs ?? samples(latest));
  const input = $derived.by(() => {
    const result: Record<string, ItemsSample> = {};
    if (!run) return result;
    for (const edge of workflow.edges.filter((e) => e.to === node.id)) {
      const sample = samples(lastPass(run.nodes, edge.from))[edge.fromPort];
      const source = workflow.nodes.find((n) => n.id === edge.from);
      if (sample) result[`${source?.name || edge.from}${edge.fromPort === 'main' ? '' : ` ${edge.fromPort}`}`] = sample;
    }
    return result;
  });
  const testBlocked = $derived(
    FULL_RUN_ONLY.includes(def.group)
      ? `${def.label} sends or changes data and runs only in a full run`
      : dirty || !workflow.id
        ? 'Save the workflow to test this step'
        : null,
  );

  $effect(() => {
    void node.id;
    test = null;
    hookUrl = null;
  });

  const errorFor = (key: string) => errors.find((e) => e.nodeId === node.id && e.field === key)?.message;

  async function runTest() {
    testing = true;
    try {
      const rows = Object.values(input).flatMap((s) => s.items);
      test = await api.post<NodeTestResult>(`/api/workflows/${workflow.id}/nodes/${node.id}/test`, rows);
      if (test.warning) notify.warning(test.warning, { key: 'node-test' });
    } catch (e) {
      notify.fail(e, 'node-test');
    } finally {
      testing = false;
    }
  }

  async function createUrl() {
    try {
      const created = await api.post<WebhookToken>(`/api/workflows/${workflow.id}/webhooks/${node.id}/token`);
      hookUrl = `${location.origin}${created.url}`;
      notify.success('Webhook URL created. Copy it now, it is shown once.', { key: 'webhook-url' });
    } catch (e) {
      notify.fail(e, 'webhook-url');
    }
  }
</script>

<aside class="inspector" aria-label="{def.label} step">
  <div class="ihead">
    <span class="iicon"><Icon size={16} /></span>
    <h2 use:tooltip={{ text: def.tooltip, placement: 'bottom-start' }}>{node.name || def.label}</h2>
    <button
      type="button"
      class="btn ghost icon"
      aria-label="Delete {node.name || def.label}"
      use:tooltip={'Delete step'}
      onclick={onremove}
    >
      <Trash2 size={15} />
    </button>
    <button type="button" class="btn ghost icon" aria-label="Close panel" onclick={onclose}><X size={15} /></button>
  </div>
  <div class="tabs" role="tablist">
    <button type="button" role="tab" aria-selected={tab === 'settings'} onclick={() => (tab = 'settings')}>Settings</button>
    <button type="button" role="tab" aria-selected={tab === 'input'} onclick={() => (tab = 'input')}>Input</button>
    <button type="button" role="tab" aria-selected={tab === 'output'} onclick={() => (tab = 'output')}>Output</button>
    <button type="button" role="tab" aria-selected={tab === 'run'} onclick={() => (tab = 'run')}>Last run</button>
  </div>
  <div class="ibody">
    {#if tab === 'settings'}
      <div class="fields">
        <div class="field">
          <label for="node-name">Name</label>
          <input id="node-name" value={node.name ?? ''} placeholder={def.label} oninput={(e) => onrename(e.currentTarget.value)} />
        </div>
        {#each fields as field (field.key)}
          <FieldControl {field} config={node.config} {options} error={errorFor(field.key)} onchange={() => onchange(field.key)} />
        {/each}
        {#if def.type === 'webhook'}
          <div class="field">
            <span class="label">Webhook URL</span>
            {#if hookUrl}
              <div class="url">
                <code>{hookUrl}</code>
                <button type="button" class="btn ghost icon" aria-label="Copy URL" use:tooltip={'Copy'} onclick={() => copyText(hookUrl!)}>
                  <Copy size={14} />
                </button>
              </div>
            {/if}
            <button
              type="button"
              class="btn"
              disabled={!workflow.id || dirty}
              use:tooltip={!workflow.id || dirty
                ? 'Save the workflow first'
                : node.config.TokenHash
                  ? 'Replaces the current URL. Callers of the old URL get 404.'
                  : 'POST JSON to this URL to start the workflow'}
              onclick={createUrl}
            >
              <Link size={14} />{node.config.TokenHash ? 'Replace URL' : 'Create URL'}
            </button>
          </div>
        {/if}
      </div>
    {:else if tab === 'input'}
      <ItemsTable
        outputs={input}
        empty={def.inputs.length
          ? 'No input yet. Run the workflow to see the rows that reach this step.'
          : 'Triggers start the workflow and have no input.'}
      />
    {:else if tab === 'output'}
      <div class="testbar">
        <span class="muted">{test ? 'Test result' : latest ? `Run ${formatDateTime(latest.startedAt)}` : ''}</span>
        <button
          type="button"
          class="btn"
          aria-busy={testing}
          disabled={!!testBlocked || testing}
          use:tooltip={testBlocked ?? 'Run this step alone with its input rows'}
          onclick={runTest}
        >
          <FlaskConical size={14} />Test step
        </button>
      </div>
      <ItemsTable outputs={output} empty="No output yet. Run the workflow or test this step." />
    {:else}
      {#if latest && run}
        <dl class="facts">
          <dt>Status</dt>
          <dd>
            <StatusMark kind={latest.status === 'Failed' ? 'err' : latest.status === 'Skipped' ? 'off' : 'ok'} label={latest.status} />
          </dd>
          <dt>Started</dt>
          <dd>{formatDateTime(latest.startedAt)}</dd>
          <dt>Duration</dt>
          <dd class="num">{latest.completedAt ? formatDuration(Date.parse(latest.completedAt) - Date.parse(latest.startedAt)) : ''}</dd>
          <dt>Rows out</dt>
          <dd class="num">{latest.rowCount}</dd>
          {#if passes.length > 1}<dt>Passes</dt>
            <dd class="num">{passes.length}</dd>{/if}
        </dl>
        {#if latest.errorMessage}<pre class="error">{latest.errorMessage}</pre>{/if}
      {:else}
        <p class="muted">{run ? 'This step did not run in the last run.' : 'The workflow has not run yet.'}</p>
      {/if}
      <button type="button" class="btn" onclick={onrun}><Play size={14} />{run ? 'Run again' : 'Run workflow'}</button>
    {/if}
  </div>
</aside>

<style>
  .inspector {
    position: absolute;
    top: 0;
    right: 0;
    bottom: 0;
    width: 420px;
    display: flex;
    flex-direction: column;
    background: var(--surface);
    border-left: 1px solid var(--line-strong);
    box-shadow: -8px 0 24px var(--shadow);
    z-index: 5;
  }
  .ihead {
    display: flex;
    align-items: center;
    gap: 10px;
    padding: 16px 20px 12px;
  }
  .ihead h2 {
    flex: 1;
    min-width: 0;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  .iicon {
    display: inline-flex;
    color: var(--accent);
  }
  .tabs {
    padding: 0 20px;
  }
  .ibody {
    flex: 1;
    overflow: auto;
    padding: 18px 20px 24px;
  }
  .testbar {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 12px;
    margin-bottom: 12px;
  }
  .url {
    display: flex;
    align-items: center;
    gap: 6px;
    margin-bottom: 8px;
  }
  .url code {
    flex: 1;
    min-width: 0;
    overflow-wrap: anywhere;
    font-size: 12px;
  }
  .label {
    font-weight: 500;
  }
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
