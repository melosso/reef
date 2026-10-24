<script lang="ts">
  let { size = 22 }: { size?: number } = $props();
</script>

<svg viewBox="0 0 24 24" width={size} height={size} aria-hidden="true">
  <rect width="24" height="24" rx="4" />
  <path
    d="M2 12c2.5-3 5-3 7.5 0s5 3 7.5 0 3.5-2 5-1M2 17c2.5-3 5-3 7.5 0s5 3 7.5 0 3.5-2 5-1"
    transform="translate(12 12) scale(0.7) translate(-12 -13.4)"
  />
</svg>

<style>
  svg {
    flex: none;
  }
  rect {
    fill: var(--accent-soft);
  }
  path {
    fill: none;
    stroke: var(--accent);
    stroke-width: 2.6;
    stroke-linecap: round;
    stroke-linejoin: round;
  }
</style>
