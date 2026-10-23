<script lang="ts">
  import Facts from '../executions/Facts.svelte';
  import { api } from '../../lib/api';
  import type { AdminMetrics } from '../../models/AdminMetrics';
  import type { GitHubRelease } from '../../models/GitHubRelease';
  import type { SystemInfo } from '../../models/SystemInfo';
  import type { VersionInfo } from '../../models/VersionInfo';
  import { formatDateTime } from '../../lib/format';

  let info = $state<SystemInfo | null>(null);
  let version = $state('');
  let latest = $state<{ tag: string; url: string } | null>(null);
  let now = $state(new Date());
  let storage = $state<AdminMetrics['system'] | null>(null);

  $effect(() => {
    api
      .get<SystemInfo>('/api/system/info')
      .then((d) => (info = d))
      .catch(() => {});
    api
      .get<AdminMetrics>('/api/admin/metrics')
      .then((d) => (storage = d?.system ?? null))
      .catch(() => {});
    api
      .get<VersionInfo>('/api/admin/version')
      .then((d) => {
        version = d?.version ?? '';
        return fetch('https://api.github.com/repos/melosso/reef/releases/latest');
      })
      .then((r): Promise<GitHubRelease | null> | null => (r?.ok ? r.json() : null))
      .then((r) => {
        if (r?.tag_name) latest = { tag: r.tag_name, url: r.html_url ?? '' };
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
        [
          'Storage',
          storage
            ? `Database ${storage.databaseSizeMb ?? 0} MB of ${storage.databaseSizeThresholdMb || 1024} MB, exports ${storage.exportsSizeMb ?? 0} MB`
            : null,
        ],
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
