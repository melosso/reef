<script lang="ts">
  import { onMount } from 'svelte';
  import { MiniMap, Panel, useNodesInitialized, useSvelteFlow, type XYPosition } from '@xyflow/svelte';
  import { Plus, Redo2, Undo2 } from '@lucide/svelte';
  import { tooltip } from '../lib/tooltip';
  import { stepIcon } from '../editor/icons';
  import type { WorkflowNodeDefinition } from '../models/WorkflowNodeDefinition';
  import { GROUPS, PALETTE_ID } from './graph';

  let {
    catalog,
    zoom,
    canUndo,
    canRedo,
    onadd,
    onundo,
    onredo,
    onclose,
    register,
  }: {
    catalog: WorkflowNodeDefinition[];
    zoom: number;
    canUndo: boolean;
    canRedo: boolean;
    onadd: (type: string) => void;
    onundo: () => void;
    onredo: () => void;
    onclose: () => void;
    register: (toFlow: (p: XYPosition) => XYPosition, reveal: (id: string, panel: number) => void) => void;
  } = $props();

  const flow = useSvelteFlow();
  let menu: HTMLElement;
  let filter = $state('');
  let active = $state(0);
  let search = $state<HTMLInputElement>();
  const matches = (d: WorkflowNodeDefinition) => `${d.label} ${d.group} ${d.tooltip}`.toLowerCase().includes(filter.trim().toLowerCase());
  const groups = $derived(
    GROUPS.map((g) => ({ name: g, nodes: catalog.filter((d) => d.group === g && matches(d)) })).filter((g) => g.nodes.length),
  );
  const flat = $derived(groups.flatMap((g) => g.nodes));

  function pick(type: string) {
    onadd(type);
    menu.hidePopover();
  }

  function onsearchkey(e: KeyboardEvent) {
    if (e.key === 'ArrowDown') {
      e.preventDefault();
      active = Math.min(flat.length - 1, active + 1);
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      active = Math.max(0, active - 1);
    } else if (e.key === 'Enter' && flat[active]) {
      e.preventDefault();
      pick(flat[active].type);
    }
  }

  // Keep the selected step clear of the panel
  function reveal(id: string, panel: number) {
    const node = flow.getNode(id);
    const pane = document.querySelector('.svelte-flow');
    if (!node || !pane) return;
    const { x, zoom: scale } = flow.getViewport();
    const left = node.position.x * scale + x;
    const right = (node.position.x + (node.measured?.width ?? 220)) * scale + x;
    const room = pane.clientWidth - panel;
    if (right > room - 32 || left < 32)
      flow.setCenter(node.position.x + (node.measured?.width ?? 220) / 2 + panel / 2 / scale, node.position.y + 40, {
        zoom: scale,
        duration: 200,
      });
  }

  const initialized = useNodesInitialized();
  const showMap = $derived(initialized.current && flow.getNodes().length > 8);
  let fitted = false;

  onMount(() => register((p) => flow.screenToFlowPosition(p), reveal));

  $effect(() => {
    if (!initialized.current || fitted) return;
    fitted = true;
    setTimeout(() => {
      flow.fitView({ padding: '48px', maxZoom: 1 }).then(() => {
        const start = flow.getNodes().find((n) => (n.data as { trigger?: boolean }).trigger);
        if (flow.getViewport().zoom < 0.6 && start) flow.setCenter(start.position.x + 520, start.position.y + 60, { zoom: 0.6 });
      });
    }, 50);
  });
</script>

{#if catalog.length && showMap}<MiniMap position="bottom-left" pannable zoomable ariaLabel="Overview" />{/if}
<Panel position="bottom-center">
  <div class="bar" role="toolbar" aria-label="Canvas">
    <button type="button" class="add" popovertarget={PALETTE_ID} use:tooltip={'Add step (/)'}><Plus size={14} />Add step</button>
    <span class="sep" aria-hidden="true"></span>
    <button type="button" class="tb icon" aria-label="Undo" disabled={!canUndo} use:tooltip={'Undo (Ctrl Z)'} onclick={onundo}>
      <Undo2 size={15} />
    </button>
    <button type="button" class="tb icon" aria-label="Redo" disabled={!canRedo} use:tooltip={'Redo (Ctrl Shift Z)'} onclick={onredo}>
      <Redo2 size={15} />
    </button>
    <span class="sep" aria-hidden="true"></span>
    <button type="button" class="tb" onclick={() => flow.fitView({ padding: '48px', maxZoom: 1, duration: 200 })}>Fit</button>
    <button
      type="button"
      class="tb num"
      use:tooltip={'Reset zoom to 100%'}
      onclick={() => flow.setViewport({ ...flow.getViewport(), zoom: 1 }, { duration: 200 })}
    >
      {Math.round(zoom * 100)}%
    </button>
  </div>
  <div
    id={PALETTE_ID}
    popover
    bind:this={menu}
    class="menu"
    aria-label="Steps"
    ontoggle={(e) => {
      if ((e as ToggleEvent).newState === 'closed') onclose();
      else {
        filter = '';
        active = 0;
        search?.focus();
      }
    }}
  >
    <input
      bind:this={search}
      bind:value={filter}
      type="search"
      class="filter"
      placeholder="Find a step"
      aria-label="Find a step"
      oninput={() => (active = 0)}
      onkeydown={onsearchkey}
    />
    {#if !flat.length}<p class="none">No step matches.</p>{/if}
    {#each groups as group (group.name)}
      <h3>{group.name}</h3>
      {#each group.nodes as def (def.type)}
        {@const Icon = stepIcon(def.icon)}
        <button
          type="button"
          draggable="true"
          class:active={flat[active]?.type === def.type}
          onmouseenter={() => (active = flat.findIndex((f) => f.type === def.type))}
          use:tooltip={{ text: def.tooltip, placement: 'right' }}
          ondragstart={(e) => {
            e.dataTransfer?.setData('application/x-reef-node', def.type);
            if (e.dataTransfer) e.dataTransfer.effectAllowed = 'copy';
            menu.hidePopover();
          }}
          onclick={() => pick(def.type)}
        >
          <Icon size={15} />{def.label}
        </button>
      {/each}
    {/each}
  </div>
</Panel>

<style>
  .bar {
    display: flex;
    align-items: center;
    gap: 4px;
    padding: 4px;
    background: var(--surface);
    border: 1px solid var(--line-strong);
    border-radius: var(--radius);
    box-shadow: 0 2px 8px var(--shadow);
  }
  .add {
    anchor-name: --workflow-add;
    white-space: nowrap;
    display: flex;
    align-items: center;
    gap: 6px;
    padding: 7px 12px;
    border: 0;
    border-radius: var(--radius);
    background: var(--accent);
    color: var(--on-accent);
    font: inherit;
    font-size: 13px;
    font-weight: 500;
    cursor: pointer;
  }
  .tb {
    display: inline-flex;
    align-items: center;
    padding: 7px 10px;
    border: 0;
    border-radius: var(--radius);
    background: transparent;
    color: var(--soft);
    font: inherit;
    font-size: 13px;
    cursor: pointer;
  }
  .tb.icon {
    padding: 7px;
  }
  .tb.num {
    font-family: var(--mono);
    font-size: 12px;
    min-width: 56px;
    justify-content: center;
  }
  .tb:hover:not(:disabled) {
    background: var(--hover);
    color: var(--ink);
  }
  .tb:disabled {
    color: var(--muted);
    opacity: 0.5;
    cursor: default;
  }
  .sep {
    width: 1px;
    height: 20px;
    background: var(--line-strong);
    margin: 0 2px;
  }
  .menu {
    position: fixed;
    inset: auto auto 76px 50%;
    transform: translateX(-50%);
    margin: 0;
  }
  @supports (position-anchor: --a) {
    .menu {
      position-anchor: --workflow-add;
      inset: auto auto anchor(top) anchor(left);
      transform: none;
      margin-bottom: 10px;
    }
  }
  .menu {
    width: 280px;
    max-height: 64vh;
    overflow: auto;
    padding: 6px;
    border: 1px solid var(--line-strong);
    border-radius: var(--radius);
    background: var(--surface);
    color: var(--ink);
    box-shadow: 0 8px 24px var(--shadow);
  }
  h3 {
    margin: 10px 10px 4px;
    font-size: 12px;
    font-weight: 600;
    color: var(--muted);
  }
  h3:first-child {
    margin-top: 4px;
  }
  .menu button {
    display: flex;
    align-items: center;
    gap: 10px;
    width: 100%;
    padding: 7px 10px;
    border: 0;
    border-radius: var(--radius);
    background: none;
    font: inherit;
    font-size: 14px;
    color: var(--ink);
    text-align: left;
    cursor: grab;
  }
  .menu button.active {
    background: var(--hover);
  }
  .filter {
    width: 100%;
    margin-bottom: 4px;
  }
  .none {
    padding: 8px 10px;
    color: var(--muted);
  }
</style>
