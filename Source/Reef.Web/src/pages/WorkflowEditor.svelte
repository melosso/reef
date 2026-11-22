<script lang="ts">
  import { onMount, untrack } from 'svelte';
  import { Background, BackgroundVariant, SvelteFlow, type Connection, type Edge, type Node, type XYPosition } from '@xyflow/svelte';
  import { Play, Settings2, X } from '@lucide/svelte';
  import Page from '../components/Page.svelte';
  import { api, ApiError, type FieldError } from '../lib/api';
  import { confirm } from '../lib/confirm.svelte';
  import { notify } from '../lib/notify';
  import { match, navigate, route, setLeaveGuard } from '../lib/router.svelte';
  import { remember } from '../lib/recent.svelte';
  import { tooltip } from '../lib/tooltip';
  import { runWorkflow } from '../lib/workflows';
  import { defaultsFor, getPath, textOf, visible } from '../editor/config';
  import { loadOptions } from '../editor/options';
  import type { Options } from '../editor/types';
  import type { Workflow } from '../models/Workflow';
  import type { WorkflowCatalog } from '../models/WorkflowCatalog';
  import type { WorkflowExecution } from '../models/WorkflowExecution';
  import type { WorkflowNode } from '../models/WorkflowNode';
  import type { WorkflowNodeDefinition } from '../models/WorkflowNodeDefinition';
  import NodeInspector from '../workflow/NodeInspector.svelte';
  import WorkflowEdgeLine from '../workflow/WorkflowEdgeLine.svelte';
  import WorkflowNodeCard from '../workflow/WorkflowNodeCard.svelte';
  import WorkflowToolbar from '../workflow/WorkflowToolbar.svelte';
  import { History, lastPass, nextId, outputsOf, PALETTE_ID } from '../workflow/graph';

  const params = $derived(match('/workflows/:id', route.path) ?? {});
  const isNew = $derived(params.id === 'new');
  const CARD_WIDTH = 220;
  const GAP = 80;
  const nodeTypes = { card: WorkflowNodeCard };
  const edgeTypes = { line: WorkflowEdgeLine };

  let catalog = $state<WorkflowNodeDefinition[]>([]);
  let doc = $state<Workflow | null>(null);
  let options = $state<Options>({});
  let run = $state<WorkflowExecution | null>(null);
  let selected = $state<string | null>(null);
  let selectedEdge = $state<string | null>(null);
  let panel = $state<'closed' | 'node' | 'settings'>('closed');
  let dirty = $state(false);
  let saving = $state(false);
  let running = $state(false);
  let errors = $state<FieldError[]>([]);
  let zoom = $state(1);
  let insertAt = $state<string | null>(null);
  let nodes = $state.raw<Node[]>([]);
  let edges = $state.raw<Edge[]>([]);
  let toFlow: ((p: XYPosition) => XYPosition) | null = null;
  const history = new History();
  let baseline = '';
  let revision = $state(0);

  const definition = (type: string) => catalog.find((d) => d.type === type);
  const selectedNode = $derived(doc?.nodes.find((n) => n.id === selected) ?? null);
  const selectedDef = $derived(selectedNode ? definition(selectedNode.type) : undefined);
  const name = $derived(doc?.name || 'Untitled workflow');
  const edgeId = (e: { from: string; fromPort: string; to: string; toPort: string }) => `${e.from}:${e.fromPort}>${e.to}:${e.toPort}`;

  onMount(() => {
    load();
    const beforeUnload = (e: BeforeUnloadEvent) => {
      if (dirty) e.preventDefault();
    };
    const refreshOptions = () => loadOptions().then((o) => (options = o));
    window.addEventListener('beforeunload', beforeUnload);
    window.addEventListener('focus', refreshOptions);
    return () => {
      window.removeEventListener('beforeunload', beforeUnload);
      window.removeEventListener('focus', refreshOptions);
      setLeaveGuard(null);
    };
  });

  $effect(() => {
    setLeaveGuard(
      dirty
        ? () =>
            confirm({
              title: 'Leave without saving?',
              message: 'Unsaved changes to this workflow are discarded.',
              confirm: 'Leave',
              danger: true,
            })
        : null,
    );
  });

  async function load() {
    try {
      const [cat, loaded, opts] = await Promise.all([
        api.get<WorkflowCatalog>('/api/workflows/catalog'),
        isNew ? Promise.resolve(blank()) : api.get<Workflow>(`/api/workflows/${params.id}`),
        loadOptions(),
      ]);
      catalog = cat.nodes;
      options = opts;
      loaded.nodes.forEach((n) => (n.config ??= {}));
      doc = loaded;
      baseline = JSON.stringify(doc);
      if (isNew) {
        dirty = true;
        panel = 'settings';
      } else {
        remember({ id: doc.id!, name: doc.name });
        await loadRun();
      }
    } catch (e) {
      notify.fail(e, 'workflow-load');
    }
  }

  function blank(): Workflow {
    return { name: '', active: false, maxPasses: 1000, nodes: [{ id: 'manual1', type: 'manual', x: 0, y: 160, config: {} }], edges: [] };
  }

  async function loadRun() {
    if (!doc?.id) return;
    const list = await api.get<WorkflowExecution[]>(`/api/workflows/${doc.id}/executions`).catch(() => []);
    run = list[0] ? await api.get<WorkflowExecution>(`/api/workflow-executions/${list[0].id}`).catch(() => null) : null;
  }

  function summary(node: WorkflowNode, def: WorkflowNodeDefinition): string {
    if (node.type === 'schedule')
      return textOf(node.config.Repeat) === 'Interval'
        ? `Every ${textOf(node.config.EveryMinutes)} min`
        : `Cron ${textOf(node.config.Cron)}`;
    const first = def.fields.find((f) => visible(f, node.config) && (f.kind === 'select' || f.kind === 'code' || f.required));
    if (!first) return '';
    const value = getPath(node.config, first.key);
    const choices = first.optionsSource ? options[first.optionsSource] : first.options;
    const picked = choices?.find((o) => textOf(o.value) === textOf(value));
    if (picked) return picked.label;
    if (value === undefined || value === null || value === '' || first.optionsSource) return '';
    const text = textOf(value).split('\n')[0];
    return first.kind === 'code' ? text : `${first.label} ${text}`;
  }

  $effect(() => {
    void revision;
    if (!doc || !catalog.length) return;
    const invalid = new Set(errors.map((e) => e.nodeId).filter(Boolean));
    const measured = new Map(untrack(() => nodes).map((n) => [n.id, n.measured]));
    nodes = doc.nodes.map((n) => {
      const def = definition(n.type);
      const pass = run ? lastPass(run.nodes, n.id) : undefined;
      const count = run?.nodes.filter((r) => r.nodeId === n.id).length ?? 0;
      return {
        id: n.id,
        type: 'card',
        position: { x: n.x, y: n.y },
        measured: measured.get(n.id),
        selected: n.id === selected,
        deletable: false,
        data: {
          label: def?.label ?? n.type,
          name: n.name || def?.label || n.type,
          icon: def?.icon ?? '',
          summary: def ? summary(n, def) : 'Unknown step type',
          inputs: def?.inputs ?? [],
          outputs: def ? outputsOf(def, n.config) : [],
          invalid: invalid.has(n.id) || !def,
          trigger: def?.group === 'Triggers',
          status: pass ? (pass.status === 'Failed' ? 'err' : pass.status === 'Skipped' ? 'off' : 'ok') : null,
          result: pass
            ? pass.status === 'Skipped'
              ? 'Skipped'
              : `${pass.rowCount} ${pass.rowCount === 1 ? 'row' : 'rows'}${count > 1 ? `, ${count} passes` : ''}`
            : '',
          error: pass?.errorMessage ?? null,
        },
      };
    });
    edges = doc.edges.map((e) => ({
      id: edgeId(e),
      type: 'line',
      source: e.from,
      sourceHandle: e.fromPort,
      target: e.to,
      targetHandle: e.toPort,
      selected: edgeId(e) === selectedEdge,
      data: { insert: openInsert, remove: removeEdge },
    }));
  });

  function changed(key: string | null = null) {
    if (!doc) return;
    history.record(baseline, key);
    baseline = JSON.stringify(doc);
    dirty = true;
    revision++;
  }

  function restore(snapshot: string | null) {
    if (!snapshot) return;
    doc = JSON.parse(snapshot) as Workflow;
    baseline = snapshot;
    dirty = true;
    if (selected && !doc.nodes.some((n) => n.id === selected)) close();
    revision++;
  }

  const undo = () => doc && restore(history.undo(JSON.stringify(doc)));
  const redo = () => doc && restore(history.redo(JSON.stringify(doc)));

  function select(id: string | null) {
    selected = id;
    selectedEdge = null;
    panel = id ? 'node' : 'closed';
  }

  function close() {
    selected = null;
    panel = 'closed';
  }

  function addNode(type: string, at?: XYPosition) {
    if (!doc) return;
    const def = definition(type);
    if (!def) return;
    const id = nextId(type, doc);
    const split = insertAt ? doc.edges.find((e) => edgeId(e) === insertAt) : undefined;
    const from = split && doc.nodes.find((n) => n.id === split.from);
    const to = split && doc.nodes.find((n) => n.id === split.to);
    const right = doc.nodes.reduce((max, n) => Math.max(max, n.x), -CARD_WIDTH);
    let position = at ?? { x: right + CARD_WIDTH + GAP, y: 160 };
    if (from && to) {
      position = { x: from.x + CARD_WIDTH + GAP, y: to.y };
      const room = position.x + CARD_WIDTH + GAP - to.x;
      if (room > 0) doc.nodes.filter((n) => n.x >= to.x).forEach((n) => (n.x += room));
    }
    doc.nodes.push({ id, type, x: position.x, y: position.y, config: defaultsFor(def.fields) });
    if (split) {
      doc.edges = doc.edges.filter((e) => e !== split);
      if (def.inputs.length) doc.edges.push({ from: split.from, fromPort: split.fromPort, to: id, toPort: def.inputs[0] });
      if (def.outputs.length) doc.edges.push({ from: id, fromPort: def.outputs[0], to: split.to, toPort: split.toPort });
    }
    insertAt = null;
    changed();
    select(id);
  }

  function openInsert(id: string) {
    insertAt = id;
    document.getElementById(PALETTE_ID)?.showPopover();
  }

  function removeNode(id: string) {
    if (!doc) return;
    doc.nodes = doc.nodes.filter((n) => n.id !== id);
    doc.edges = doc.edges.filter((e) => e.from !== id && e.to !== id);
    errors = errors.filter((e) => e.nodeId !== id);
    changed();
    close();
  }

  function removeEdge(id: string) {
    if (!doc) return;
    doc.edges = doc.edges.filter((e) => edgeId(e) !== id);
    selectedEdge = null;
    changed();
  }

  function connect(c: Connection) {
    if (!doc || !c.source || !c.target || c.source === c.target) return;
    const edge = { from: c.source, fromPort: c.sourceHandle ?? 'main', to: c.target, toPort: c.targetHandle ?? 'main' };
    if (doc.edges.some((e) => edgeId(e) === edgeId(edge))) return;
    doc.edges.push(edge);
    changed();
  }

  function onfieldchange(nodeId: string, key: string) {
    errors = errors.filter((e) => !(e.nodeId === nodeId && e.field === key));
    changed(`${nodeId}:${key}`);
  }

  async function save(): Promise<boolean> {
    if (!doc || saving) return false;
    saving = true;
    try {
      const saved = doc.id ? await api.put<Workflow>(`/api/workflows/${doc.id}`, doc) : await api.post<Workflow>('/api/workflows', doc);
      errors = [];
      dirty = false;
      setLeaveGuard(null);
      notify.success(`${saved.name} saved`, { key: 'workflow-save' });
      remember({ id: saved.id!, name: saved.name });
      if (!doc.id) {
        navigate(`/workflows/${saved.id}`, true);
        return true;
      }
      saved.nodes.forEach((n) => (n.config ??= {}));
      doc = saved;
      baseline = JSON.stringify(doc);
      revision++;
      return true;
    } catch (e) {
      errors = e instanceof ApiError ? e.errors : [];
      revision++;
      notify.fail(e, 'workflow-save');
      const first = errors.find((x) => x.nodeId);
      if (first?.nodeId) select(first.nodeId);
      else if (errors.length) panel = 'settings';
      return false;
    } finally {
      saving = false;
    }
  }

  async function runNow() {
    if ((dirty || !doc?.id) && !(await save())) return;
    if (!doc?.id) return;
    running = true;
    try {
      const started = await runWorkflow(doc);
      if (!started) return;
      run = await api.get<WorkflowExecution>(`/api/workflow-executions/${started.id}`);
      remember({ id: doc.id, name, status: run.status === 'Failed' ? 'err' : 'ok' });
      revision++;
    } catch (e) {
      notify.fail(e, 'workflow-run');
    } finally {
      running = false;
    }
  }

  function onkeydown(e: KeyboardEvent) {
    const active = document.activeElement as HTMLElement | null;
    const typing = ['INPUT', 'TEXTAREA', 'SELECT'].includes(active?.tagName ?? '') || active?.isContentEditable;
    const mod = e.ctrlKey || e.metaKey;
    if (mod && e.key.toLowerCase() === 's') {
      e.preventDefault();
      save();
      return;
    }
    if (typing || document.querySelector('dialog[open]')) return;
    if (mod && e.key.toLowerCase() === 'z') {
      e.preventDefault();
      if (e.shiftKey) redo();
      else undo();
    } else if (mod && e.key.toLowerCase() === 'y') {
      e.preventDefault();
      redo();
    } else if (e.key === 'Delete' || e.key === 'Backspace') {
      if (selected) {
        e.preventDefault();
        removeNode(selected);
      } else if (selectedEdge) {
        e.preventDefault();
        removeEdge(selectedEdge);
      }
    } else if (e.key === 'Escape' && panel !== 'closed') close();
  }

  function ondrop(e: DragEvent) {
    const type = e.dataTransfer?.getData('application/x-reef-node');
    if (!type || !toFlow) return;
    e.preventDefault();
    const p = toFlow({ x: e.clientX, y: e.clientY });
    addNode(type, { x: Math.round((p.x - CARD_WIDTH / 2) / 8) * 8, y: Math.round((p.y - 40) / 8) * 8 });
  }
</script>

<svelte:window {onkeydown} />

<Page
  crumbs={[
    { label: 'Workflows', href: '/workflows' },
    {
      label: name,
      value: doc?.name ?? '',
      onrename: doc
        ? (value) => {
            doc!.name = value;
            changed('name');
          }
        : undefined,
    },
  ]}
>
  {#snippet status()}
    <span class="state">
      <svg viewBox="0 0 8 8" aria-hidden="true"><circle cx="4" cy="4" r="3" class:dashed={dirty} /></svg>
      {saving ? 'Saving' : dirty ? 'Unsaved changes' : 'Saved'}
    </span>
  {/snippet}
  {#snippet actions()}
    <button type="button" class="btn" aria-label="Workflow settings" use:tooltip={'Workflow settings'} onclick={() => (panel = 'settings')}>
      <Settings2 size={15} /><span class="wide-label">Workflow settings</span>
    </button>
    <button
      type="button"
      class="btn"
      aria-busy={running}
      disabled={running || saving}
      use:tooltip={dirty ? 'Save and run from the manual trigger' : 'Run from the manual trigger'}
      onclick={runNow}
    >
      <Play size={14} />Run
    </button>
    <button type="button" class="btn primary" aria-busy={saving} disabled={!dirty || saving} use:tooltip={'Ctrl S'} onclick={save}>
      Save
    </button>
  {/snippet}

  <div class="editor">
    {#if doc && catalog.length}
      <div
        class="canvas"
        class:inset={panel !== 'closed'}
        role="application"
        aria-label="Workflow canvas"
        ondragover={(e) => {
          if (e.dataTransfer?.types.includes('application/x-reef-node')) {
            e.preventDefault();
            e.dataTransfer.dropEffect = 'copy';
          }
        }}
        {ondrop}
      >
        <SvelteFlow
          bind:nodes
          bind:edges
          {nodeTypes}
          {edgeTypes}
          deleteKey={null}
          minZoom={0.15}
          maxZoom={1.6}
          snapGrid={[8, 8]}
          proOptions={{ hideAttribution: true }}
          onconnect={connect}
          onnodeclick={({ node }) => select(node.id)}
          onedgeclick={({ edge }) => {
            selectedEdge = edge.id;
            selected = null;
            if (panel === 'node') panel = 'closed';
            revision++;
          }}
          onpaneclick={() => {
            selectedEdge = null;
            if (panel === 'node') close();
            revision++;
          }}
          onnodedragstop={({ nodes: moved }) => {
            let moves = false;
            for (const m of moved) {
              const target = doc!.nodes.find((n) => n.id === m.id);
              if (target && (target.x !== m.position.x || target.y !== m.position.y)) {
                target.x = m.position.x;
                target.y = m.position.y;
                moves = true;
              }
            }
            if (moves) changed();
          }}
          onmove={(_, viewport) => (zoom = viewport.zoom)}
        >
          <Background variant={BackgroundVariant.Dots} gap={20} size={1.2} patternColor="var(--dot)" bgColor="var(--bg)" />
          <WorkflowToolbar
            {catalog}
            {zoom}
            canUndo={revision >= 0 && history.canUndo}
            canRedo={revision >= 0 && history.canRedo}
            onadd={addNode}
            onundo={undo}
            onredo={redo}
            onclose={() => setTimeout(() => (insertAt = null))}
            register={(t) => (toFlow = t)}
          />
        </SvelteFlow>
      </div>

      {#if panel === 'node' && selectedNode && selectedDef}
        <NodeInspector
          workflow={doc}
          node={selectedNode}
          def={selectedDef}
          {options}
          {errors}
          {run}
          {dirty}
          onchange={(key) => onfieldchange(selectedNode!.id, key)}
          onrename={(value) => {
            selectedNode!.name = value || null;
            changed(`${selectedNode!.id}:name`);
          }}
          onremove={() => removeNode(selectedNode!.id)}
          onclose={close}
          onrun={runNow}
        />
      {:else if panel === 'settings'}
        <aside class="settings" aria-label="Workflow settings">
          <div class="ihead">
            <span class="iicon"><Settings2 size={16} /></span>
            <h2>Workflow settings</h2>
            <button type="button" class="btn ghost icon" aria-label="Close panel" onclick={close}><X size={15} /></button>
          </div>
          <div class="ibody fields">
            <div class="field">
              <label for="wf-name">Name</label>
              <input
                id="wf-name"
                value={doc.name}
                aria-invalid={errors.some((e) => e.field === 'Name') ? 'true' : undefined}
                oninput={(e) => {
                  doc!.name = e.currentTarget.value;
                  changed('name');
                }}
              />
            </div>
            <label class="check" use:tooltip={'Schedule and webhook triggers start the workflow only when it is on'}>
              <input
                type="checkbox"
                checked={doc.active}
                onchange={(e) => {
                  doc!.active = e.currentTarget.checked;
                  changed();
                }}
              />On
            </label>
            <div class="field">
              <label for="wf-passes" use:tooltip={'Stops a run when one step runs more often than this, to end loops without an exit'}>
                Pass limit per step
              </label>
              <input
                id="wf-passes"
                type="number"
                class="num"
                value={doc.maxPasses}
                aria-invalid={errors.some((e) => e.field === 'MaxPasses') ? 'true' : undefined}
                oninput={(e) => {
                  doc!.maxPasses = Number(e.currentTarget.value);
                  changed('passes');
                }}
              />
            </div>
          </div>
        </aside>
      {/if}
    {/if}
  </div>
</Page>

<style>
  .editor {
    position: relative;
    flex: 1;
    min-height: 0;
  }
  .canvas {
    position: absolute;
    inset: 0;
  }
  .canvas.inset {
    right: 420px;
  }
  .state {
    display: inline-flex;
    align-items: center;
    gap: 6px;
  }
  .state svg {
    width: 8px;
    height: 8px;
  }
  .state circle {
    fill: none;
    stroke: var(--ok);
    stroke-width: 1.5;
  }
  .state circle.dashed {
    stroke: var(--muted);
    stroke-dasharray: 2 1.5;
  }
  .settings {
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
  }
  .iicon {
    display: inline-flex;
    color: var(--accent);
  }
  .ibody {
    flex: 1;
    overflow: auto;
    padding: 18px 20px 24px;
  }
  .check {
    display: flex;
    align-items: center;
    gap: 8px;
  }
  .canvas :global(.svelte-flow) {
    --xy-edge-stroke: var(--edge);
    --xy-edge-stroke-width: 1.5;
    --xy-connectionline-stroke: var(--accent);
    background: var(--bg);
  }
  .canvas :global(.svelte-flow__node.selected .card),
  .canvas :global(.svelte-flow__node:focus-visible) {
    outline: none;
  }
  .canvas :global(.svelte-flow__node:focus-visible .card) {
    outline: 2px solid var(--accent);
    outline-offset: 2px;
  }
</style>
