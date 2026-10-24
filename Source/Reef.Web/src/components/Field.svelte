<script lang="ts">
  import type { Snippet } from 'svelte';
  import { Info } from '@lucide/svelte';
  import { tooltip } from '../lib/tooltip';

  let {
    label,
    id,
    hint,
    error,
    required = false,
    children,
  }: {
    label: string;
    id: string;
    hint?: string;
    error?: string;
    required?: boolean;
    children: Snippet;
  } = $props();
</script>

<div class="field">
  <label for={id} class:invalid={!!error} use:tooltip={error ? { text: error, placement: 'top-start' } : null}>
    {label}{#if required}<span class="req" aria-hidden="true">*</span>{/if}
    {#if hint}<span class="hint" tabindex="-1" use:tooltip={{ text: hint, placement: 'right' }}><Info size={13} /></span>{/if}
  </label>
  {@render children()}
</div>

<style>
  label {
    display: flex;
    align-items: center;
    gap: 4px;
  }
  .req {
    color: var(--err);
  }
  .hint {
    color: var(--muted);
    display: inline-flex;
    cursor: help;
  }
  .invalid {
    color: var(--err);
  }
</style>
