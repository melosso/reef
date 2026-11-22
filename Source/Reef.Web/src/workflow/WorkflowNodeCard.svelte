<script lang="ts">
  import { Handle, Position, type NodeProps } from '@xyflow/svelte';
  import StatusMark, { type StatusKind } from '../components/StatusMark.svelte';
  import { tooltip } from '../lib/tooltip';
  import { stepIcon } from '../editor/icons';

  interface CardData {
    label: string;
    name: string;
    icon: string;
    summary: string;
    inputs: string[];
    outputs: string[];
    invalid: boolean;
    trigger: boolean;
    status: StatusKind | null;
    result: string;
    error: string | null;
  }

  let { data, selected }: NodeProps = $props();
  const card = $derived(data as unknown as CardData);
  const Icon = $derived(stepIcon(card.icon));
  const offset = (index: number, count: number) => `${((index + 1) * 100) / (count + 1)}%`;
</script>

<div
  class="card"
  class:selected
  class:invalid={card.invalid}
  class:failed={card.status === 'err'}
  class:trigger={card.trigger}
  aria-invalid={card.invalid ? 'true' : undefined}
>
  {#each card.inputs as port, i (port)}
    <Handle type="target" position={Position.Left} id={port} style="top: {offset(i, card.inputs.length)}" />
    {#if card.inputs.length > 1}<span class="port in" style="top: {offset(i, card.inputs.length)}">{port}</span>{/if}
  {/each}
  <div class="head">
    <span class="icon"><Icon size={16} strokeWidth={1.75} /></span>
    <span class="title">{card.name}</span>
  </div>
  {#if card.summary}<div class="body">{card.summary}</div>{/if}
  <div class="foot" use:tooltip={card.error}>
    {#if card.status}<StatusMark kind={card.status} />{/if}
    <span class="text">{card.status ? card.result : 'Not run yet'}</span>
  </div>
  {#each card.outputs as port, i (port)}
    <Handle type="source" position={Position.Right} id={port} style="top: {offset(i, card.outputs.length)}" />
    {#if card.outputs.length > 1}<span class="port out" style="top: {offset(i, card.outputs.length)}">{port}</span>{/if}
  {/each}
</div>

<style>
  .card {
    position: relative;
    width: 220px;
    background: var(--surface);
    border: 1px solid var(--line-strong);
    border-radius: var(--radius);
    color: var(--ink);
    font-family: var(--font);
    cursor: pointer;
  }
  .card.selected {
    border: 2px solid var(--accent);
    margin: -1px;
  }
  .card.invalid,
  .card.failed:not(.selected) {
    border-color: var(--err);
  }
  .head {
    display: flex;
    align-items: center;
    gap: 10px;
    padding: 11px 14px;
    border-bottom: 1px solid var(--line);
  }
  .icon {
    display: inline-flex;
    color: var(--soft);
  }
  .selected .icon {
    color: var(--accent);
  }
  .invalid .icon {
    color: var(--err);
  }
  .title {
    font-size: 14px;
    font-weight: 600;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  .body {
    padding: 9px 14px;
    font-size: 13px;
    color: var(--muted);
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  .foot {
    display: flex;
    align-items: center;
    gap: 6px;
    padding: 7px 14px;
    border-top: 1px solid var(--line);
    font-size: 12px;
    color: var(--muted);
  }
  .foot .text {
    flex: 1;
    min-width: 0;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  .port {
    position: absolute;
    transform: translateY(-50%);
    font-size: 11px;
    color: var(--muted);
    pointer-events: none;
    white-space: nowrap;
  }
  .port.out {
    left: calc(100% + 10px);
  }
  .port.in {
    right: calc(100% + 10px);
  }
  .card :global(.svelte-flow__handle) {
    width: 10px;
    height: 10px;
    background: var(--surface);
    border: 1.5px solid var(--edge);
  }
  .card :global(.svelte-flow__handle:hover),
  .card :global(.svelte-flow__handle.connectingto) {
    border-color: var(--accent);
    background: var(--accent-soft);
  }
</style>
