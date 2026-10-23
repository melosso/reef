<script lang="ts">
  export type StatusKind = 'ok' | 'err' | 'warn' | 'run' | 'idle' | 'off';

  let { kind, label }: { kind: StatusKind; label?: string } = $props();
</script>

<span class="status {kind}">
  <svg viewBox="0 0 10 10" aria-hidden="true">
    {#if kind === 'err'}
      <path d="M2 2l6 6M8 2l-6 6" />
    {:else if kind === 'warn'}
      <path d="M5 1.5 9 8.5H1Z" />
    {:else if kind === 'run'}
      <circle cx="5" cy="5" r="3.5" stroke-dasharray="3 2" />
    {:else if kind === 'off'}
      <path d="M2 5h6" />
    {:else}
      <circle cx="5" cy="5" r="3.5" />
    {/if}
  </svg>
  {#if label}<span>{label}</span>{/if}
</span>

<style>
  .status {
    display: inline-flex;
    align-items: center;
    gap: 6px;
    white-space: nowrap;
  }
  svg {
    width: 10px;
    height: 10px;
    flex: none;
    fill: none;
    stroke-width: 1.5;
    stroke-linejoin: round;
  }
  .ok svg {
    stroke: var(--ok);
  }
  .err svg {
    stroke: var(--err);
  }
  .warn svg {
    stroke: var(--warn);
  }
  .run svg {
    stroke: var(--accent);
    animation: spin 1.6s linear infinite;
  }
  .idle svg,
  .off svg {
    stroke: var(--muted);
  }
  .off {
    color: var(--muted);
  }
  @keyframes spin {
    to {
      transform: rotate(360deg);
    }
  }
  @media (prefers-reduced-motion: reduce) {
    .run svg {
      animation: none;
    }
  }
</style>
