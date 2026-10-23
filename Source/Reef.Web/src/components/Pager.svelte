<script lang="ts">
  import { ChevronLeft, ChevronRight } from '@lucide/svelte';
  import { formatNumber } from '../lib/format';

  let {
    page = $bindable(),
    pageSize = $bindable(),
    total,
    pages,
    sizes = [25, 50, 100],
  }: {
    page: number;
    pageSize: number;
    total: number;
    pages: number;
    sizes?: number[];
  } = $props();
</script>

<div class="pager">
  <span class="muted"><span class="num">{formatNumber(total)}</span> entries</span>
  <span class="spacer"></span>
  <label class="inline" for="pager-size">Rows per page</label>
  <select
    id="pager-size"
    class="size"
    value={String(pageSize)}
    onchange={(e) => {
      pageSize = Number(e.currentTarget.value);
      page = 1;
    }}
  >
    {#each sizes as n}<option value={String(n)}>{n}</option>{/each}
  </select>
  <button type="button" class="btn icon" aria-label="Previous page" disabled={page <= 1} onclick={() => page--}
    ><ChevronLeft size={15} /></button
  >
  <span class="num">{page} / {Math.max(1, pages)}</span>
  <button type="button" class="btn icon" aria-label="Next page" disabled={page >= pages} onclick={() => page++}
    ><ChevronRight size={15} /></button
  >
</div>

<style>
  .pager {
    display: flex;
    align-items: center;
    gap: 8px;
  }
  .spacer {
    flex: 1;
  }
  .inline {
    margin: 0;
    font-weight: 400;
    color: var(--muted);
  }
  .size {
    width: auto !important;
  }
</style>
