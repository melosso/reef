<script lang="ts">
  import Page from '../components/Page.svelte';
  import StatusMark from '../components/StatusMark.svelte';
  import { api } from '../lib/api';
  import { formatDateTime, formatDuration, formatNumber, formatRelative } from '../lib/format';
  import { notify } from '../lib/notify';
  import { navigate } from '../lib/router.svelte';
  import { tooltip } from '../lib/tooltip';
  import type { AttentionAnomaly } from '../models/AttentionAnomaly';
  import type { DashboardAttention } from '../models/DashboardAttention';

  let attention = $state<DashboardAttention | null>(null);
  let approvals = $state(0);

  async function load() {
    try {
      const [data, pending] = await Promise.all([
        api.get<DashboardAttention>('/api/dashboard/attention'),
        api.get<{ count: number }>('/api/email-approvals/count').catch(() => ({ count: 0 })),
      ]);
      attention = data;
      approvals = pending?.count ?? 0;
    } catch (e) {
      notify.fail(e, 'dashboard');
    }
  }

  $effect(() => {
    load();
  });

  function open(e: MouseEvent, href: string) {
    e.preventDefault();
    e.stopPropagation();
    navigate(href);
  }

  const runHref = (kind: string, id: number) => `/executions/${kind}/${id}`;
  const describe = (a: AttentionAnomaly) =>
    a.reason === 'rows'
      ? `${formatNumber(a.value)} rows, usually ${formatNumber(a.median)}`
      : `Took ${formatDuration(a.value)}, usually ${formatDuration(a.median)}`;
  const issues = $derived((attention?.failed.length ?? 0) + (attention?.anomalies.length ?? 0) + (approvals ? 1 : 0));
</script>

<Page crumbs={[{ label: 'Dashboard' }]}>
  <div class="page-body">
    {#if attention}
      <section class="panel">
        <div class="panel-head">
          <h2>Needs attention</h2>
          <span class="spacer"></span>
          <span class="muted" use:tooltip={'Runs started in the last 24 hours'}
            >{formatNumber(attention.last24h.runs)}
            {attention.last24h.runs === 1 ? 'run' : 'runs'}, {formatNumber(attention.last24h.failed)} failed</span
          >
        </div>
        {#if issues}
          <table class="data">
            <tbody>
              {#each attention.failed as f (f.kind + f.executionId)}
                <tr class="clickable" onclick={() => navigate(runHref(f.kind, f.executionId))}>
                  <td class="mark"><StatusMark kind="err" label="Failed" /></td>
                  <td
                    ><a
                      class="strong"
                      href="/profiles/{f.kind}/{f.profileId}"
                      onclick={(e) => open(e, `/profiles/${f.kind}/${f.profileId}`)}>{f.profileName}</a
                    ></td
                  >
                  <td class="detail" use:tooltip={f.error}>{f.error}</td>
                  <td class="when"><span use:tooltip={formatDateTime(f.startedAt)}>{formatRelative(f.startedAt)}</span></td>
                </tr>
              {/each}
              {#each attention.anomalies as a (a.kind + a.executionId)}
                <tr class="clickable" onclick={() => navigate(runHref(a.kind, a.executionId))}>
                  <td class="mark"><StatusMark kind="warn" label={a.reason === 'rows' ? 'Fewer rows' : 'Slower'} /></td>
                  <td
                    ><a
                      class="strong"
                      href="/profiles/{a.kind}/{a.profileId}"
                      onclick={(e) => open(e, `/profiles/${a.kind}/${a.profileId}`)}>{a.profileName}</a
                    ></td
                  >
                  <td class="detail">{describe(a)}</td>
                  <td class="when"><span use:tooltip={formatDateTime(a.startedAt)}>{formatRelative(a.startedAt)}</span></td>
                </tr>
              {/each}
              {#if approvals}
                <tr class="clickable" onclick={() => navigate('/approvals')}>
                  <td class="mark"><StatusMark kind="idle" label="Waiting" /></td>
                  <td><a class="strong" href="/approvals">Email approvals</a></td>
                  <td class="detail">{formatNumber(approvals)} {approvals === 1 ? 'email waits' : 'emails wait'} for approval</td>
                  <td class="when"></td>
                </tr>
              {/if}
            </tbody>
          </table>
        {:else}
          <div class="panel-body"><StatusMark kind="ok" label="All clear. Every profile's latest run succeeded." /></div>
        {/if}
      </section>

      <section class="panel">
        <div class="panel-head">
          <h2>Up next</h2>
          <span class="spacer"></span><a href="/jobs">All jobs</a>
        </div>
        {#if attention.upNext.length}
          <table class="data">
            <tbody>
              {#each attention.upNext as job (job.id)}
                <tr class="clickable" onclick={() => navigate(`/jobs/${job.id}`)}>
                  <td class="strong">{job.name}</td>
                  <td class="when"><span use:tooltip={formatDateTime(job.nextRunTime)}>{formatRelative(job.nextRunTime)}</span></td>
                </tr>
              {/each}
            </tbody>
          </table>
        {:else}
          <div class="panel-body muted">No jobs scheduled in the next 24 hours.</div>
        {/if}
      </section>
    {/if}
  </div>
</Page>

<style>
  .spacer {
    flex: 1;
  }
  .strong {
    font-weight: 500;
    color: var(--ink);
    text-decoration: none;
  }
  td.mark {
    width: 1%;
    white-space: nowrap;
  }
  td.detail {
    color: var(--soft);
    max-width: 0;
    width: 60%;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  td.when {
    width: 1%;
    white-space: nowrap;
    text-align: right;
    color: var(--muted);
  }
</style>
