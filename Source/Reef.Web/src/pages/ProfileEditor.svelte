<script lang="ts">
  import { onMount } from 'svelte';
  import { Background, BackgroundVariant, SvelteFlow, type Edge, type Node, type XYPosition } from '@xyflow/svelte';
  import { Play, Settings2, Trash2, X } from '@lucide/svelte';
  import Page from '../components/Page.svelte';
  import { api, ApiError, type FieldError } from '../lib/api';
  import { notify } from '../lib/notify';
  import { runProfile } from '../lib/profiles';
  import { remember } from '../lib/recent.svelte';
  import { match, navigate, route, setLeaveGuard } from '../lib/router.svelte';
  import { confirm } from '../lib/confirm.svelte';
  import { theme } from '../lib/theme.svelte';
  import { tooltip } from '../lib/tooltip';
  import CanvasToolbar from '../editor/CanvasToolbar.svelte';
  import FieldControl from '../editor/FieldControl.svelte';
  import StepActions from '../editor/StepActions.svelte';
  import StepNode from '../editor/StepNode.svelte';
  import { defaultsFor, getPath, textOf, visible } from '../editor/config';
  import { stepIcon } from '../editor/icons';
  import { loadOptions } from '../editor/options';
  import { lastRunLabel, loadLastRun, stepStat, type LastRun } from '../editor/stepStats';
  import type { Catalog, Config, DocNode, Options, ProfileDoc } from '../editor/types';

  const params = $derived(match('/profiles/:kind/new', route.path) ?? match('/profiles/:kind/:id', route.path) ?? {});

  const API = '/api/profile-documents';
  const CARD_WIDTH = 220;
  const CARD_GAP = 60;
  const nodeTypes = { step: StepNode };

  const kind = $derived((params.kind === 'import' ? 'import' : 'export') as 'export' | 'import');
  const isNew = $derived(!params.id);

  let catalog = $state<Catalog | null>(null);
  let doc = $state<ProfileDoc | null>(null);
  let options = $state<Options>({});
  let selected = $state<string | null>(null);
  let panel = $state<'closed' | 'step' | 'settings'>('closed');
  let tab = $state<'settings' | 'test'>('settings');
  let dirty = $state(false);
  let saving = $state(false);
  let running = $state(false);
  let errors = $state<FieldError[]>([]);
  let zoom = $state(1);
  let lastRun = $state<LastRun | null>(null);
  let nodes = $state.raw<Node[]>([]);
  let edges = $state.raw<Edge[]>([]);
  let toFlow: ((p: XYPosition) => XYPosition) | null = null;
  let fit: (() => void) | null = null;
  let nameInput = $state<HTMLInputElement>();

  const definition = (type: string) => catalog!.nodes.find((n) => n.type === type)!;
  const ordered = () => [...(doc?.nodes ?? [])].sort((a, b) => definition(a.type).rank - definition(b.type).rank);
  const selectedNode = $derived(doc?.nodes.find((n) => n.id === selected) ?? null);
  const selectedDef = $derived(selectedNode && catalog ? definition(selectedNode.type) : null);
  const palette = $derived(catalog ? catalog.nodes.filter((d) => !d.required).sort((a, b) => a.rank - b.rank) : []);
  const present = $derived(new Set(doc?.nodes.map((n) => n.type) ?? []));
  const name = $derived(textOf(doc?.settings.Name) || 'Untitled profile');

  onMount(() => {
    load();
    const beforeUnload = (e: BeforeUnloadEvent) => {
      if (dirty) e.preventDefault();
    };
    window.addEventListener('beforeunload', beforeUnload);
    return () => {
      window.removeEventListener('beforeunload', beforeUnload);
      setLeaveGuard(null);
    };
  });

  $effect(() => {
    setLeaveGuard(
      dirty
        ? () => confirm({ title: 'Leave without saving?', message: 'Unsaved changes to this profile are discarded.', confirm: 'Leave', danger: true })
        : null,
    );
  });

  async function load() {
    try {
      const [cat, loaded, opts] = await Promise.all([
        api.get<Catalog>(`${API}/${kind}/catalog`),
        api.get<ProfileDoc>(isNew ? `${API}/${kind}/new` : `${API}/${kind}/${params.id}`),
        loadOptions(),
      ]);
      catalog = cat;
      options = opts;
      loaded.nodes.forEach((n) => (n.config ??= {}));
      doc = loaded;
      if (isNew) {
        dirty = true;
        panel = 'settings';
        tidy(false);
        setTimeout(() => nameInput?.focus(), 50);
      } else if (doc.id) {
        remember({ kind, id: doc.id, name });
        loadLastRun(kind, doc.id).then((r) => (lastRun = r));
      }
      setTimeout(() => fit?.(), 60);
    } catch (e) {
      notify.fail(e, 'editor-load');
    }
  }

  function summary(node: DocNode): string {
    const def = definition(node.type);
    const config = node.config ?? {};
    const first = def.fields.find((f) => visible(f, config) && (f.kind === 'select' || f.key === 'Query'));
    if (!first) return def.tooltip;
    const value = getPath(config, first.key);
    const choices = first.optionsSource ? options[first.optionsSource] : first.options;
    const match = choices?.find((o) => textOf(o.value) === textOf(value));
    if (match) return match.label;
    if (value === undefined || value === null || value === '' || first.optionsSource) return '';
    return textOf(value).split('\n')[0];
  }

  $effect(() => {
    if (!doc || !catalog) return;
    const invalid = new Set(errors.map((e) => e.nodeId).filter(Boolean));
    const list = ordered();
    nodes = list.map((n) => {
      const def = definition(n.type);
      return {
        id: n.id,
        type: 'step',
        position: { x: n.x, y: n.y },
        selected: n.id === selected,
        deletable: false,
        data: {
          label: def.label,
          icon: def.icon,
          summary: summary(n),
          invalid: invalid.has(n.id),
          stat: stepStat(lastRun, n.type),
          lastRun: lastRunLabel(lastRun),
        },
      };
    });
    edges = list
      .slice(1)
      .map((n, i) => ({ id: `${list[i].id}-${n.id}`, source: list[i].id, target: n.id, type: 'default', selectable: false }));
  });

  function markDirty() {
    dirty = true;
  }

  function tidy(mark = true) {
    if (!doc) return;
    ordered().forEach((node, index) => {
      node.x = 40 + index * (CARD_WIDTH + CARD_GAP);
      node.y = 160;
    });
    if (mark) markDirty();
    setTimeout(() => fit?.(), 30);
  }

  function select(id: string | null) {
    selected = id;
    panel = id ? 'step' : 'closed';
    tab = 'settings';
  }

  function openSettings() {
    selected = null;
    panel = 'settings';
    tab = 'settings';
  }

  function addNode(type: string, at?: XYPosition) {
    if (!doc) return;
    const existing = doc.nodes.find((n) => n.type === type);
    if (existing) return select(existing.id);
    const def = definition(type);
    doc.nodes.push({ id: type, type, x: at?.x ?? 0, y: at?.y ?? 0, config: defaultsFor(def.fields) });
    if (!at) tidy(false);
    markDirty();
    select(type);
  }

  function removeNode(id: string) {
    if (!doc) return;
    const node = doc.nodes.find((n) => n.id === id);
    if (!node) return;
    const def = definition(node.type);
    if (def.required) {
      notify.info(`${def.label} is part of every ${kind} profile and stays on the canvas.`);
      return;
    }
    doc.nodes = doc.nodes.filter((n) => n.id !== id);
    errors = errors.filter((e) => e.nodeId !== id);
    markDirty();
    select(null);
  }

  function onfieldchange(nodeId: string | null, key: string) {
    errors = errors.filter((e) => !(e.nodeId == nodeId && e.field === key) && !(nodeId === null && !e.nodeId && e.field === key));
    markDirty();
  }

  async function save(): Promise<boolean> {
    if (!doc || saving) return false;
    saving = true;
    try {
      const saved = doc.id ? await api.put<ProfileDoc>(`${API}/${kind}/${doc.id}`, doc) : await api.post<ProfileDoc>(`${API}/${kind}`, doc);
      errors = [];
      dirty = false;
      setLeaveGuard(null);
      notify.success(`${textOf(saved.settings.Name)} saved`, { key: 'editor-save' });
      remember({ kind, id: saved.id!, name: textOf(saved.settings.Name) });
      if (!doc.id) {
        navigate(`/profiles/${kind}/${saved.id}`, true);
        return true;
      }
      saved.nodes.forEach((n) => (n.config ??= {}));
      doc = saved;
      if (selected && !saved.nodes.some((n) => n.id === selected)) select(null);
      return true;
    } catch (e) {
      errors = e instanceof ApiError ? e.errors : [];
      notify.fail(e, 'editor-save');
      const first = errors.find((x) => x.nodeId) ?? errors[0];
      if (first?.nodeId) select(first.nodeId);
      else if (first?.field === 'Name') nameInput?.focus();
      else if (first?.field) openSettings();
      return false;
    } finally {
      saving = false;
    }
  }

  async function run() {
    if ((dirty || !doc?.id) && !(await save())) return;
    if (!doc?.id) return;
    running = true;
    try {
      const result = await runProfile(kind, doc.id, name);
      lastRun = await loadLastRun(kind, doc.id);
      const failed = /fail/i.test(result.status ?? '') || result.success === false;
      remember({ kind, id: doc.id, name, status: failed ? 'err' : 'ok' });
    } catch (e) {
      notify.fail(e, `run-${kind}-${doc.id}`);
    } finally {
      running = false;
    }
  }

  function flatten() {
    return api.post<Config>(`${API}/${kind}/flatten${doc?.id ? `?id=${doc.id}` : ''}`, doc);
  }

  function errorFor(nodeId: string | null, key: string) {
    return errors.find((e) => (nodeId ? e.nodeId === nodeId : !e.nodeId) && e.field === key)?.message;
  }

  function onkeydown(e: KeyboardEvent) {
    const typing =
      ['INPUT', 'TEXTAREA', 'SELECT'].includes((document.activeElement as HTMLElement)?.tagName) ||
      (document.activeElement as HTMLElement)?.isContentEditable;
    if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 's') {
      e.preventDefault();
      save();
      return;
    }
    if (typing || document.querySelector('dialog[open]')) return;
    if ((e.key === 'Delete' || e.key === 'Backspace') && selected) {
      e.preventDefault();
      removeNode(selected);
    } else if (e.key === 'Escape' && panel !== 'closed') {
      selected = null;
      panel = 'closed';
    }
  }

  function ondrop(e: DragEvent) {
    const type = e.dataTransfer?.getData('application/x-reef-node');
    if (!type || !toFlow) return;
    e.preventDefault();
    const p = toFlow({ x: e.clientX, y: e.clientY });
    addNode(type, { x: Math.round((p.x - CARD_WIDTH / 2) / 8) * 8, y: Math.round((p.y - 40) / 8) * 8 });
  }

  const stepFields = $derived(selectedDef && selectedNode ? selectedDef.fields.filter((f) => visible(f, selectedNode.config ?? {})) : []);
  const settingsFields = $derived(catalog && doc ? catalog.settings.filter((f) => f.key !== 'Name' && visible(f, doc!.settings)) : []);
</script>

<svelte:window {onkeydown} />

<Page crumbs={[{ label: 'Profiles', href: '/profiles' }, { label: name }]}>
  {#snippet status()}
    <span class="state">
      <svg viewBox="0 0 8 8" aria-hidden="true"><circle cx="4" cy="4" r="3" class:dashed={dirty} /></svg>
      {saving ? 'Saving' : dirty ? 'Unsaved changes' : 'Saved'}
    </span>
  {/snippet}
  {#snippet actions()}
    <button type="button" class="btn" onclick={openSettings}><Settings2 size={15} />Profile settings</button>
    <button
      type="button"
      class="btn"
      aria-busy={running}
      disabled={running || saving}
      use:tooltip={dirty ? 'Save and run the profile' : 'Run the profile'}
      onclick={run}><Play size={14} />Run</button
    >
    <button type="button" class="btn primary" aria-busy={saving} disabled={!dirty || saving} use:tooltip={'Ctrl S'} onclick={save}
      >Save</button
    >
  {/snippet}

  <div class="editor">
    {#if doc && catalog}
      <div
        class="canvas"
        class:inset={panel !== 'closed'}
        role="application"
        aria-label="Profile canvas"
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
          colorMode={theme.choice}
          nodesConnectable={false}
          deleteKey={null}
          minZoom={0.3}
          maxZoom={1.6}
          snapGrid={[8, 8]}
          proOptions={{ hideAttribution: true }}
          onnodeclick={({ node }) => select(node.id)}
          onpaneclick={() => {
            selected = null;
            if (panel === 'step') panel = 'closed';
          }}
          onnodedragstop={({ nodes: moved }) => {
            for (const m of moved) {
              const target = doc!.nodes.find((n) => n.id === m.id);
              if (target && (target.x !== m.position.x || target.y !== m.position.y)) {
                target.x = m.position.x;
                target.y = m.position.y;
                markDirty();
              }
            }
          }}
          onmove={(_, viewport) => (zoom = viewport.zoom)}
        >
          <Background variant={BackgroundVariant.Dots} gap={20} size={1.2} patternColor="var(--dot)" bgColor="var(--bg)" />
          <CanvasToolbar
            {palette}
            {present}
            {zoom}
            onadd={addNode}
            ontidy={() => tidy()}
            register={(t, f) => {
              toFlow = t;
              fit = f;
            }}
          />
        </SvelteFlow>
      </div>

      {#if panel !== 'closed'}
        <aside class="inspector" aria-label={panel === 'step' ? `${selectedDef?.label} settings` : 'Profile settings'}>
          <div class="ihead">
            {#if panel === 'step' && selectedDef}
              {@const Icon = stepIcon(selectedDef.icon)}
              <span class="iicon"><Icon size={16} /></span>
              <h2 use:tooltip={{ text: selectedDef.tooltip, placement: 'bottom-start' }}>{selectedDef.label}</h2>
              {#if !selectedDef.required}
                <button
                  type="button"
                  class="btn ghost icon"
                  aria-label="Remove {selectedDef.label}"
                  use:tooltip={'Remove step'}
                  onclick={() => removeNode(selectedNode!.id)}><Trash2 size={15} /></button
                >
              {/if}
            {:else}
              <span class="iicon"><Settings2 size={16} /></span>
              <h2>Profile settings</h2>
            {/if}
            <button
              type="button"
              class="btn ghost icon"
              aria-label="Close panel"
              onclick={() => {
                selected = null;
                panel = 'closed';
              }}><X size={15} /></button
            >
          </div>
          <div class="tabs" role="tablist">
            <button type="button" role="tab" aria-selected={tab === 'settings'} onclick={() => (tab = 'settings')}>Settings</button>
            <button type="button" role="tab" aria-selected={tab === 'test'} onclick={() => (tab = 'test')}
              >{panel === 'settings' ? 'Webhooks' : 'Test'}</button
            >
          </div>
          <div class="ibody">
            {#if tab === 'settings'}
              <div class="fields">
                {#if panel === 'settings'}
                  <div class="field">
                    <label for="profile-name" class:invalid={!!errorFor(null, 'Name')} use:tooltip={errorFor(null, 'Name') ?? null}
                      >Name<span class="req" aria-hidden="true">*</span></label
                    >
                    <input
                      id="profile-name"
                      bind:this={nameInput}
                      value={textOf(doc.settings.Name)}
                      aria-invalid={errorFor(null, 'Name') ? 'true' : undefined}
                      oninput={(e) => {
                        doc!.settings.Name = e.currentTarget.value;
                        onfieldchange(null, 'Name');
                      }}
                    />
                  </div>
                  {#if doc.code}<p class="muted">Code <span class="num">{doc.code}</span></p>{/if}
                  {#each settingsFields as field (field.key)}
                    <FieldControl
                      {field}
                      config={doc.settings}
                      {options}
                      error={errorFor(null, field.key)}
                      onchange={() => onfieldchange(null, field.key)}
                    />
                  {/each}
                {:else if selectedNode}
                  {#each stepFields as field (field.key)}
                    <FieldControl
                      {field}
                      config={selectedNode.config!}
                      {options}
                      error={errorFor(selectedNode.id, field.key)}
                      onchange={() => onfieldchange(selectedNode!.id, field.key)}
                    />
                  {/each}
                {/if}
              </div>
            {:else}
              {@const step = panel === 'settings' ? 'settings' : (selectedNode?.type ?? '')}
              {#key `${kind}:${step}`}
                <StepActions
                  {kind}
                  id={doc.id}
                  {step}
                  config={panel === 'settings' ? doc.settings : (selectedNode?.config ?? {})}
                  {dirty}
                  {flatten}
                />
              {/key}
            {/if}
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
    right: 400px;
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
  .inspector {
    position: absolute;
    top: 0;
    right: 0;
    bottom: 0;
    width: 400px;
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
  .inspector .tabs {
    padding: 0 20px;
  }
  .ibody {
    flex: 1;
    overflow: auto;
    padding: 18px 20px 24px;
  }
  .req {
    color: var(--err);
  }
  .invalid {
    color: var(--err);
  }
  .canvas :global(.svelte-flow) {
    --xy-edge-stroke: var(--edge);
    --xy-edge-stroke-width: 1.5;
    background: var(--bg);
  }
  .canvas :global(.svelte-flow__node.selected .step),
  .canvas :global(.svelte-flow__node:focus-visible) {
    outline: none;
  }
  .canvas :global(.svelte-flow__node:focus-visible .step) {
    outline: 2px solid var(--accent);
    outline-offset: 2px;
  }
</style>
