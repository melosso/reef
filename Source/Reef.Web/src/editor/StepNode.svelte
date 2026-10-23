<script lang="ts">
  import { Handle, Position, type NodeProps } from '@xyflow/svelte';
  import StatusMark from '../components/StatusMark.svelte';
  import { formatDuration } from '../lib/format';
  import { tooltip } from '../lib/tooltip';
  import { stepIcon } from './icons';
  import type { StepStat } from './stepStats';

  let { data, selected }: NodeProps = $props();
  const Icon = $derived(stepIcon(data.icon as string));
  const stat = $derived(data.stat as StepStat | null);
</script>

<div class="step" class:selected class:invalid={data.invalid} aria-invalid={data.invalid ? 'true' : undefined}>
  <Handle type="target" position={Position.Left} isConnectable={false} />
  <div class="head">
    <span class="icon"><Icon size={16} strokeWidth={1.75} /></span>
    <span class="title">{data.label}</span>
  </div>
  <div class="body">{data.summary || 'Not set up'}</div>
  {#if stat}
    <div class="foot" use:tooltip={data.lastRun as string}>
      {#if stat.status}<StatusMark kind={stat.status} />{/if}
      <span class="text">{stat.text}</span>
      {#if stat.ms !== null && stat.ms !== undefined}<span class="num">{formatDuration(stat.ms)}</span>{/if}
    </div>
  {/if}
  <Handle type="source" position={Position.Right} isConnectable={false} />
</div>

<style>
  .step {
    width: 220px;
    background: var(--surface);
    border: 1px solid var(--line-strong);
    border-radius: var(--radius);
    color: var(--ink);
    font-family: var(--font);
    cursor: pointer;
  }
  .step.selected {
    border: 2px solid var(--accent);
    margin: -1px;
  }
  .step.invalid {
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
    color: var(--muted);
  }
  .foot .text {
    flex: 1;
    min-width: 0;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  .step :global(.svelte-flow__handle) {
    width: 8px;
    height: 8px;
    background: var(--surface);
    border: 1.5px solid var(--edge);
  }
</style>
