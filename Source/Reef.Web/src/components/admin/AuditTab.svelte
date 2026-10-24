<script lang="ts">
  import Pager from '../Pager.svelte';
  import { api } from '../../lib/api';
  import type { AuditEntry } from '../../models/AuditEntry';
  import type { PagedResult } from '../../models/PagedResult';
  import { queryString } from '../../lib/query';
  import { urlPaging } from '../../lib/paging.svelte';
  import { formatDateTime } from '../../lib/format';
  import { notify } from '../../lib/notify';

  let entityType = $state('');
  let action = $state('');
  const paging = urlPaging(50);
  let total = $state(0);
  let pages = $state(1);
  let logs = $state<AuditEntry[]>([]);

  $effect(() => {
    const q = queryString({ page: paging.page, pageSize: paging.size, entityType, action });
    api
      .get<PagedResult<AuditEntry>>(`/api/admin/audit-logs?${q}`)
      .then((r) => {
        logs = r?.data ?? [];
        total = r?.totalCount ?? 0;
        pages = r?.totalPages ?? 1;
      })
      .catch((e) => notify.fail(e, 'audit'));
  });

  const pick = (l: AuditEntry, k: string) => l[k] ?? l[k.charAt(0).toLowerCase() + k.slice(1)];
</script>

<div class="toolbar">
  <select
    aria-label="Entity type"
    value={entityType}
    onchange={(e) => {
      entityType = e.currentTarget.value;
      paging.page = 1;
    }}
  >
    <option value="">All entity types</option><option value="Connection">Connection</option><option value="Profile">Profile</option><option
      value="User">User</option
    ><option value="ApiKey">API key</option>
  </select>
  <select
    aria-label="Action"
    value={action}
    onchange={(e) => {
      action = e.currentTarget.value;
      paging.page = 1;
    }}
  >
    <option value="">All actions</option>{#each ['Created', 'Updated', 'Deleted', 'Executed'] as a (a)}<option value={a}>{a}</option>{/each}
  </select>
</div>
<div class="table-wrap">
  <table class="data">
    <thead><tr><th>Time</th><th>Entity</th><th>Action</th><th>By</th><th>Changes</th></tr></thead>
    <tbody>
      {#each logs as l, i (i)}
        <tr>
          <td class="nowrap">{formatDateTime(pick(l, 'Timestamp'))}</td>
          <td
            >{pick(l, 'EntityType')}
            <span class="num muted">{pick(l, 'EntityCode') || pick(l, 'EntityId')}</span>{#if pick(l, 'EntityName')}<div
                class="muted small"
              >
                {pick(l, 'EntityName')}
              </div>{/if}</td
          >
          <td>{pick(l, 'Action')}</td>
          <td>{pick(l, 'PerformedBy')}</td>
          <td
            >{#if pick(l, 'Changes')}<details>
                <summary>Show</summary>
                <pre class="code">{pick(l, 'Changes')}</pre>
              </details>{/if}</td
          >
        </tr>
      {:else}
        <tr><td colspan="5" class="empty">No audit entries</td></tr>
      {/each}
    </tbody>
  </table>
</div>
<Pager bind:page={paging.page} bind:pageSize={paging.size} {total} {pages} />

<style>
  .nowrap {
    white-space: nowrap;
  }
  .small {
    font-size: 12px;
  }
  summary {
    cursor: pointer;
    color: var(--muted);
  }
</style>
