<script lang="ts">
  let { items }: { items: [string, string | number | null | undefined][] } = $props();
</script>

<dl class="facts">
  {#each items.filter(([, v]) => v !== null && v !== undefined && v !== '') as [label, value] (label)}
    <div>
      <dt>{label}</dt>
      <dd class:num={typeof value === 'number'}>{value}</dd>
    </div>
  {/each}
</dl>

<style>
  .facts {
    display: grid;
    grid-template-columns: repeat(auto-fill, minmax(140px, 1fr));
    gap: 12px;
    margin: 0;
  }
  .facts div {
    min-width: 0;
  }
  dt {
    font-size: 12px;
    color: var(--muted);
  }
  dd {
    margin: 2px 0 0;
    font-weight: 500;
    overflow-wrap: anywhere;
  }
</style>
