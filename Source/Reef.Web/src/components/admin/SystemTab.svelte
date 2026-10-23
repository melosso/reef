<script lang="ts">
  import Facts from '../executions/Facts.svelte';
  import { api } from '../../lib/api';
  import { formatDateTime } from '../../lib/format';

  let info = $state<any>(null);
  let version = $state('');
  let latest = $state<{ tag: string; url: string } | null>(null);
  let now = $state(new Date());

  $effect(() => {
    api
      .get('/api/system/info')
      .then((d) => (info = d))
      .catch(() => {});
    api
      .get('/api/admin/version')
      .then((d) => {
        version = d?.version ?? '';
        return fetch('https://api.github.com/repos/melosso/reef/releases/latest');
      })
      .then((r) => (r?.ok ? r.json() : null))
      .then((r) => {
        if (r?.tag_name) latest = { tag: r.tag_name, url: r.html_url };
      })
      .catch(() => {});
    const timer = setInterval(() => (now = new Date()), 1000);
    return () => clearInterval(timer);
  });

  const clean = (v: string) => v.replace(/^v/i, '').split(/[+-]/)[0];
  const outdated = $derived(
    !!latest &&
      !!version &&
      clean(latest.tag) !== clean(version) &&
      clean(latest.tag).localeCompare(clean(version), undefined, { numeric: true }) > 0,
  );
</script>

<section class="panel">
  <div class="panel-head"><h2>System</h2></div>
  <div class="panel-body fields">
    <Facts
      items={[
        ['Version', info?.version ?? version],
        ['Environment', info?.environment ?? 'Production'],
        ['Uptime', info?.uptime?.days !== undefined ? `${info.uptime.days} days` : null],
        ['Server time', now.toLocaleTimeString()],
        ['Server date', now.toLocaleDateString()],
        ['Reported at', info?.timestamp ? formatDateTime(info.timestamp) : null],
      ]}
    />
    {#if latest}
      <p class="muted">
        {#if outdated}Version <span class="num">{latest.tag}</span> is available.
          <a href={latest.url} target="_blank" rel="noopener noreferrer">Release notes</a>
        {:else}Reef is up to date. Latest release <span class="num">{latest.tag}</span>.{/if}
      </p>
    {/if}
  </div>
</section>
