<script lang="ts">
  let { size = 22 }: { size?: number } = $props();
</script>

<svg viewBox="0 0 24 24" width={size} height={size} aria-hidden="true">
  <path d="M2 12c2.5-3 5-3 7.5 0s5 3 7.5 0 3.5-2 5-1" />
  <path d="M2 17c2.5-3 5-3 7.5 0s5 3 7.5 0 3.5-2 5-1" />
</svg>

<style>
  svg {
    flex: none;
    fill: none;
    stroke: var(--accent);
    stroke-width: 2;
    stroke-linecap: round;
    stroke-linejoin: round;
  }
</style>
