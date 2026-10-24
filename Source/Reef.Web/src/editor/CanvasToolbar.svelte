<script lang="ts">
  import { onMount } from 'svelte';
  import { Panel, useSvelteFlow, type XYPosition } from '@xyflow/svelte';
  import { Plus } from '@lucide/svelte';
  import { tooltip } from '../lib/tooltip';
  import { stepIcon } from './icons';
  import type { NodeDefinition } from './types';

  let {
    palette,
    present,
    zoom,
    onadd,
    ontidy,
    register,
  }: {
    palette: NodeDefinition[];
    present: Set<string>;
    zoom: number;
    onadd: (type: string, at?: XYPosition) => void;
    ontidy: () => void;
    register: (toFlow: (p: XYPosition) => XYPosition, fit: () => void) => void;
  } = $props();

  const flow = useSvelteFlow();
  let menu: HTMLElement;

  onMount(() =>
    register(
      (p) => flow.screenToFlowPosition(p),
      () => flow.fitView({ padding: 0.25, maxZoom: 1, duration: 200 }),
    ),
  );

  function pick(type: string) {
    menu.hidePopover();
    onadd(type);
  }
</script>

<Panel position="bottom-center">
  <div class="bar" role="toolbar" aria-label="Canvas">
    <button type="button" class="add" popovertarget="step-menu"><Plus size={14} />Add step</button>
    <button type="button" class="tb" use:tooltip={'Line the steps up in run order'} onclick={ontidy}>Tidy up</button>
    <span class="sep" aria-hidden="true"></span>
    <button type="button" class="tb" onclick={() => flow.fitView({ padding: 0.25, maxZoom: 1, duration: 200 })}>Fit</button>
    <button
      type="button"
      class="tb num"
      use:tooltip={'Reset zoom to 100%'}
      onclick={() => flow.setViewport({ ...flow.getViewport(), zoom: 1 }, { duration: 200 })}>{Math.round(zoom * 100)}%</button
    >
  </div>
  <div id="step-menu" popover bind:this={menu} class="menu" aria-label="Steps">
    {#each palette as def (def.type)}
      {@const Icon = stepIcon(def.icon)}
      {@const used = present.has(def.type)}
      <button
        type="button"
        draggable={!used}
        disabled={used}
        use:tooltip={{ text: used ? `${def.label} is on the canvas` : def.tooltip, placement: 'right' }}
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
    display: flex;
    align-items: center;
    gap: 6px;
    padding: 7px 12px;
    border: 0;
    border-radius: var(--radius);
    background: var(--ink);
    color: var(--bg);
    font: inherit;
    font-size: 13px;
    font-weight: 500;
    cursor: pointer;
  }
  .tb {
    padding: 7px 10px;
    border: 0;
    border-radius: var(--radius);
    background: transparent;
    color: var(--soft);
    font: inherit;
    font-size: 13px;
    cursor: pointer;
  }
  .tb.num {
    font-family: var(--mono);
    font-size: 12px;
    min-width: 56px;
  }
  .tb:hover {
    background: var(--hover);
    color: var(--ink);
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
    width: 260px;
    max-height: 60vh;
    overflow: auto;
    padding: 6px;
    border: 1px solid var(--line-strong);
    border-radius: var(--radius);
    background: var(--surface);
    color: var(--ink);
    box-shadow: 0 8px 24px var(--shadow);
  }
  .menu button {
    display: flex;
    align-items: center;
    gap: 10px;
    width: 100%;
    padding: 8px 10px;
    border: 0;
    border-radius: var(--radius);
    background: none;
    font: inherit;
    color: var(--ink);
    text-align: left;
    cursor: grab;
  }
  .menu button:hover:not(:disabled) {
    background: var(--hover);
  }
  .menu button:disabled {
    color: var(--muted);
    cursor: default;
  }
</style>
