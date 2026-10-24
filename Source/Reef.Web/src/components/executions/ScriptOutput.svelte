<script lang="ts">
  let { title, exitCode, stdout, stderr }: { title: string; exitCode?: number | null; stdout?: string | null; stderr?: string | null } =
    $props();
  const shown = $derived((exitCode !== null && exitCode !== undefined) || !!stdout || !!stderr);
</script>

{#if shown}
  <section class="block">
    <h4>
      {title}{#if exitCode !== null && exitCode !== undefined}
        <span class="num" class:bad={exitCode !== 0}>exit {exitCode}</span>{/if}
    </h4>
    {#if stdout}<pre class="code">{stdout.trim()}</pre>{/if}
    {#if stderr}<pre class="code err">{stderr.trim()}</pre>{/if}
  </section>
{/if}

<style>
  .block {
    display: flex;
    flex-direction: column;
    gap: 6px;
  }
  .bad,
  .err {
    color: var(--err);
  }
</style>
