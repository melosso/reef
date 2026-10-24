<script lang="ts">
  import Page from '../components/Page.svelte';
  import { route } from '../lib/router.svelte';
</script>

<Page crumbs={[{ label: 'Page not found' }]}>
  <div class="empty-state">
    <h1>Page not found</h1>
    <p>No page exists at <span class="num">{route.path}</span>.</p>
    <a class="btn" href="/dashboard">Open dashboard</a>
  </div>
</Page>
