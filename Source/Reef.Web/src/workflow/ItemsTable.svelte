<script lang="ts">
  import { formatBytes } from '../lib/format';
  import type { ItemsSample } from '../models/ItemsSample';

  let { outputs, empty }: { outputs: Record<string, ItemsSample>; empty: string } = $props();

  const ports = $derived(Object.keys(outputs));
  let picked = $state<string | null>(null);
  const port = $derived(picked && ports.includes(picked) ? picked : (ports[0] ?? null));
  const sample = $derived(port ? outputs[port] : null);
  const columns = $derived(sample ? (sample.columns.length ? sample.columns.map((c) => c.name) : Object.keys(sample.items[0] ?? {})) : []);

  function cell(value: unknown): string {
    if (value === null || value === undefined) return '';
    return typeof value === 'object' ? JSON.stringify(value) : String(value);
  }
</script>

{#if !sample}
  <p class="muted">{empty}</p>
{:else}
  {#if ports.length > 1}
    <div class="ports" role="tablist" aria-label="Outputs">
      {#each ports as p (p)}
        <button type="button" role="tab" aria-selected={p === port} onclick={() => (picked = p)}>
          {p} <span class="num">{outputs[p].total}</span>
        </button>
      {/each}
    </div>
  {/if}
  <p class="muted">
    {#if sample.total > sample.items.length}First <span class="num">{sample.items.length}</span> of
    {/if}<span class="num">{sample.total}</span>
    {sample.total === 1 ? 'row' : 'rows'}{#if sample.files.length}, <span class="num">{sample.files.length}</span>
      {sample.files.length === 1 ? 'file' : 'files'}{/if}
  </p>
  {#if sample.items.length}
    <div class="table-wrap">
      <table class="data">
        <thead>
          <tr>
            {#each columns as c (c)}<th>{c}</th>{/each}
          </tr>
        </thead>
        <tbody>
          {#each sample.items as row, i (i)}
            <tr>
              {#each columns as c (c)}<td>{cell(row[c])}</td>{/each}
            </tr>
          {/each}
        </tbody>
      </table>
    </div>
  {/if}
  {#if sample.files.length}
    <ul class="files">
      {#each sample.files as f (f.path)}
        <li><span>{f.name}</span><span class="num muted">{formatBytes(f.size)}</span></li>
      {/each}
    </ul>
  {/if}
{/if}

<style>
  .ports {
    display: flex;
    gap: 4px;
    flex-wrap: wrap;
    margin-bottom: 8px;
  }
  .ports button {
    padding: 4px 8px;
    border: 1px solid var(--line-strong);
    border-radius: var(--radius);
    background: var(--surface);
    color: var(--soft);
    font: inherit;
    font-size: 13px;
    cursor: pointer;
  }
  .ports button[aria-selected='true'] {
    border-color: var(--accent);
    color: var(--ink);
  }
  .table-wrap {
    max-width: 100%;
    overflow: auto;
    margin-top: 8px;
  }
  td {
    max-width: 220px;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  .files {
    list-style: none;
    margin: 8px 0 0;
    padding: 0;
  }
  .files li {
    display: flex;
    justify-content: space-between;
    gap: 12px;
    padding: 6px 0;
    border-bottom: 1px solid var(--line);
  }
</style>
