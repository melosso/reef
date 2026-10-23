<script lang="ts">
  import type { Snippet } from 'svelte';
  import { X } from '@lucide/svelte';

  let {
    title,
    onclose,
    children,
    footer,
    wide = false,
  }: {
    title: string;
    onclose: () => void;
    children: Snippet;
    footer?: Snippet;
    wide?: boolean;
  } = $props();

  function onkeydown(e: KeyboardEvent) {
    if (e.key === 'Escape' && !document.querySelector('dialog[open]')) onclose();
  }
</script>

<svelte:window {onkeydown} />

<aside class="sheet" class:wide aria-label={title}>
  <div class="sheet-head">
    <h2>{title}</h2>
    <button type="button" class="btn ghost icon" aria-label="Close panel" onclick={onclose}><X size={16} /></button>
  </div>
  <div class="sheet-body">{@render children()}</div>
  {#if footer}<div class="sheet-foot">{@render footer()}</div>{/if}
</aside>

<style>
  .wide {
    width: min(760px, 100vw);
  }
</style>
