<script lang="ts">
  import { api } from '../../lib/api';
  import { formatNumber } from '../../lib/format';
  import { notify } from '../../lib/notify';

  let m = $state<any>({});

  $effect(() => {
    api
      .get('/api/admin/metrics')
      .then((d) => (m = d ?? {}))
      .catch((e) => notify.fail(e, 'metrics'));
  });

  const week = $derived(m.recentStats?.last7Days ?? {});
</script>

<div class="cards">
  <div class="stat"><span class="label">Profiles</span><span class="value">{formatNumber(m.profiles?.total ?? 0)}</span></div>
  <div class="stat"><span class="label">Active profiles</span><span class="value">{formatNumber(m.profiles?.active ?? 0)}</span></div>
  <div class="stat"><span class="label">Executions</span><span class="value">{formatNumber(m.executions?.total ?? 0)}</span></div>
  <div class="stat">
    <span class="label">Success rate</span><span class="value">{Math.round((m.executions?.successRate ?? 0) * 10) / 10}%</span>
  </div>
  <div class="stat">
    <span class="label">Failed, last 24 hours</span><span class="value">{formatNumber(m.executions?.failedLast24h ?? 0)}</span>
  </div>
  <div class="stat"><span class="label">Uptime in days</span><span class="value">{m.system?.uptimeDays ?? 0}</span></div>
</div>
<h3>Last 7 days</h3>
<div class="cards">
  <div class="stat"><span class="label">Executions</span><span class="value">{formatNumber(week.total ?? 0)}</span></div>
  <div class="stat"><span class="label">Succeeded</span><span class="value">{formatNumber(week.success ?? 0)}</span></div>
  <div class="stat"><span class="label">Failed</span><span class="value">{formatNumber(week.failed ?? 0)}</span></div>
  <div class="stat">
    <span class="label">Average duration</span><span class="value"
      >{week.avgDurationMs !== undefined ? `${formatNumber(week.avgDurationMs)} ms` : '0 ms'}</span
    >
  </div>
</div>
<h3>Storage</h3>
<div class="cards">
  <div class="stat"><span class="label">Database</span><span class="value">{m.system?.databaseSizeMb ?? 0} MB</span></div>
  <div class="stat"><span class="label">Exports</span><span class="value">{m.system?.exportsSizeMb ?? 0} MB</span></div>
</div>
