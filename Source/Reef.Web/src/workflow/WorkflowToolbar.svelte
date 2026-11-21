<script lang="ts">
  import { onMount } from 'svelte';
  import { Panel, useNodesInitialized, useSvelteFlow, type XYPosition } from '@xyflow/svelte';
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
    register: (toFlow: (p: XYPosition) => XYPosition) => void;
  } = $props();

  const flow = useSvelteFlow();
  let menu: HTMLElement;
  const groups = $derived(GROUPS.map((g) => ({ name: g, nodes: catalog.filter((d) => d.group === g) })).filter((g) => g.nodes.length));

  const initialized = useNodesInitialized();
  let fitted = false;

  onMount(() => register((p) => flow.screenToFlowPosition(p)));

  $effect(() => {
    if (!initialized.current || fitted) return;
    fitted = true;
    setTimeout(() => flow.fitView({ padding: '48px', maxZoom: 1 }), 50);
  });
</script>

<Panel position="bottom-center">
  <div class="bar" role="toolbar" aria-label="Canvas">
    <button type="button" class="add" popovertarget={PALETTE_ID}><Plus size={14} />Add step</button>
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
    }}
  >
    {#each groups as group (group.name)}
      <h3>{group.name}</h3>
      {#each group.nodes as def (def.type)}
        {@const Icon = stepIcon(def.icon)}
        <button
          type="button"
          draggable="true"
          use:tooltip={{ text: def.tooltip, placement: 'right' }}
          ondragstart={(e) => {
            e.dataTransfer?.setData('application/x-reef-node', def.type);
            if (e.dataTransfer) e.dataTransfer.effectAllowed = 'copy';
            menu.hidePopover();
          }}
          onclick={() => {
            onadd(def.type);
            menu.hidePopover();
          }}
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
  .menu button:hover {
    background: var(--hover);
  }
</style>
