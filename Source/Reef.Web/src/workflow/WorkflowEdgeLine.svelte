<script lang="ts">
  import { BaseEdge, EdgeLabel, getBezierPath, getSmoothStepPath, type EdgeProps } from '@xyflow/svelte';
  import { Plus, X } from '@lucide/svelte';
  import { tooltip } from '../lib/tooltip';

  let { id, sourceX, sourceY, targetX, targetY, sourcePosition, targetPosition, selected, data, markerEnd }: EdgeProps = $props();
  const geometry = $derived(
    targetX < sourceX
      ? getSmoothStepPath({ sourceX, sourceY, targetX, targetY, sourcePosition, targetPosition, borderRadius: 6, offset: 28 })
      : getBezierPath({ sourceX, sourceY, targetX, targetY, sourcePosition, targetPosition }),
  );
  const actions = $derived(data as { insert: (id: string) => void; remove: (id: string) => void });
</script>

<BaseEdge path={geometry[0]} {markerEnd} class={selected ? 'picked' : ''} interactionWidth={24} />
<EdgeLabel x={geometry[1]} y={geometry[2]} transparent>
  <div class="tools" class:shown={selected}>
    <button type="button" aria-label="Insert a step here" use:tooltip={'Insert step'} onclick={() => actions.insert(id)}>
      <Plus size={13} />
    </button>
    <button type="button" aria-label="Delete connection" use:tooltip={'Delete connection'} onclick={() => actions.remove(id)}>
      <X size={13} />
    </button>
  </div>
</EdgeLabel>

<style>
  .tools {
    display: flex;
    gap: 2px;
    opacity: 0;
    transition: opacity 120ms ease-out;
  }
  .tools.shown,
  .tools:hover,
  .tools:focus-within {
    opacity: 1;
  }
  button {
    display: inline-flex;
    padding: 3px;
    border: 1px solid var(--line-strong);
    border-radius: var(--radius);
    background: var(--surface);
    color: var(--soft);
    cursor: pointer;
  }
  button:hover {
    color: var(--ink);
    border-color: var(--accent);
  }
  :global(.svelte-flow__edge-path.picked) {
    stroke: var(--accent);
  }
</style>
