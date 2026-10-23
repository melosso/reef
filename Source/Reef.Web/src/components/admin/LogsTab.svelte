<script lang="ts">
  import Pager from '../Pager.svelte';
  import { api } from '../../lib/api';
  import type { LogEntry } from '../../models/LogEntry';
  import type { PagedResult } from '../../models/PagedResult';
  import { queryString } from '../../lib/query';
  import { formatDateTime } from '../../lib/format';
  import { notify } from '../../lib/notify';

  let level = $state('');
  let page = $state(1);
  let pageSize = $state(100);
  let total = $state(0);
  let pages = $state(1);
  let logs = $state<LogEntry[]>([]);
  let live = $state(false);

  async function load() {
    const q = queryString({ page, pageSize, minimumLevel: level });
    try {
      const r = await api.get<PagedResult<LogEntry>>(`/api/admin/logs?${q}`);
      logs = r?.data ?? [];
      total = r?.totalCount ?? 0;
      pages = r?.totalPages ?? 1;
    } catch (e) {
      notify.fail(e, 'logs');
    }
  }

  $effect(() => {
    load();
  });

  $effect(() => {
    if (!live) return;
    const timer = setInterval(load, 5000);
    return () => clearInterval(timer);
  });

  const levelClass = (l: string) => (/error|fatal/i.test(l) ? 'err' : /warn/i.test(l) ? 'warn' : 'muted');
</script>

<div class="toolbar">
  <select
    aria-label="Minimum level"
    value={level}
    onchange={(e) => {
      level = e.currentTarget.value;
      page = 1;
    }}
  >
    <option value="">All levels</option>
    {#each ['Debug', 'Information', 'Warning', 'Error', 'Fatal'] as l (l)}<option value={l}>{l}</option>{/each}
  </select>
  <label class="check"><input type="checkbox" role="switch" bind:checked={live} />Refresh every 5 seconds</label>
  <span class="spacer"></span>
  <button type="button" class="btn" onclick={load}>Refresh</button>
</div>
<div class="table-wrap">
  <table class="data">
    <thead><tr><th>Time</th><th>Level</th><th>Message</th></tr></thead>
    <tbody>
      {#each logs as log, i (i)}
        {@const lvl = log.level ?? log.Level ?? ''}
        <tr>
          <td class="nowrap">{formatDateTime(log.timestamp ?? log.Timestamp)}</td>
          <td class={levelClass(lvl)}>{lvl}</td>
          <td class="msg"
            >{log.message ?? log.Message}{#if log.exception ?? log.Exception}<pre class="code">{log.exception ??
                  log.Exception}</pre>{/if}</td
          >
        </tr>
      {:else}
        <tr><td colspan="3" class="empty">No log entries</td></tr>
      {/each}
    </tbody>
  </table>
</div>
<Pager bind:page bind:pageSize {total} {pages} sizes={[50, 100, 250]} />

<style>
  .spacer {
    flex: 1;
  }
  .nowrap {
    white-space: nowrap;
  }
  .msg {
    word-break: break-word;
  }
  .err {
    color: var(--err);
  }
  .warn {
    color: var(--warn);
  }
</style>
