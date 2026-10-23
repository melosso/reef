<script lang="ts">
  import { Copy, Download } from '@lucide/svelte';
  import StatusMark from '../StatusMark.svelte';
  import Facts from './Facts.svelte';
  import ScriptOutput from './ScriptOutput.svelte';
  import { api } from '../../lib/api';
  import { formatBytes, formatDateTime, formatDuration, formatNumber } from '../../lib/format';
  import { copyText, notify } from '../../lib/notify';
  import { statusKind, statusLabel } from '../../lib/status';

  let { id, profiles, jobs }: { id: number; profiles: any[]; jobs: any[] } = $props();

  let exec = $state<any>(null);
  let connection = $state<any>(null);
  let destination = $state<any>(null);
  let splits = $state<any[]>([]);
  let failures = $state<any[]>([]);

  $effect(() => {
    load(id);
  });

  async function load(executionId: number) {
    exec = null;
    connection = null;
    destination = null;
    splits = [];
    failures = [];
    try {
      const e = await api.get(`/api/executions/${executionId}`);
      exec = e;
      const profile = profiles.find((p) => p.id === e.profileId);
      if (profile?.connectionId) connection = await api.get(`/api/connections/${profile.connectionId}`).catch(() => null);
      if (profile?.outputDestinationId) destination = await api.get(`/api/destinations/${profile.outputDestinationId}`).catch(() => null);
      if (e.wasSplit) splits = await api.get<any[]>(`/api/profiles/executions/${executionId}/splits`).catch(() => []);
      if ((e.splitFailureCount ?? 0) > 0) failures = await api.get<any[]>(`/api/executions/${executionId}/errors`).catch(() => []);
    } catch (err) {
      notify.fail(err);
    }
  }

  const profileName = $derived(
    exec ? (profiles.find((p) => p.id === exec.profileId)?.name ?? exec.profileName ?? `Profile ${exec.profileId}`) : '',
  );
  const jobName = $derived(exec?.jobId ? (jobs.find((j) => j.id === exec.jobId)?.name ?? `Job ${exec.jobId}`) : null);
  const token = $derived(exec?.outputPath ? /exports\/[\d-]+\/([^/]+)\//.exec(exec.outputPath)?.[1] : null);

  function downloadLog() {
    const wrapped = [
      {
        id: exec.id,
        profile: exec.profileCode,
        timestamp: exec.startedAt,
        domain: location.hostname,
        details: { ...exec, profileId: undefined },
      },
    ];
    const a = document.createElement('a');
    a.href = URL.createObjectURL(new Blob([JSON.stringify(wrapped, null, 2)], { type: 'application/json' }));
    a.download = `log_${exec.id}.json`;
    a.click();
    URL.revokeObjectURL(a.href);
  }
</script>

{#if !exec}
  <p class="muted">Loading execution</p>
{:else}
  <div class="head">
    <div>
      <h3>{profileName}</h3>
      <p class="muted">
        Execution <span class="num">{exec.id}</span> · Triggered by {exec.triggeredBy || 'System'}{#if jobName}
          · Job {jobName}{/if}
      </p>
    </div>
    <StatusMark kind={statusKind(exec.status)} label={statusLabel(exec.status)} />
  </div>

  <Facts
    items={[
      ['Started', formatDateTime(exec.startedAt)],
      ['Completed', exec.completedAt ? formatDateTime(exec.completedAt) : exec.status === 'Running' ? 'Running' : null],
      ['Duration', formatDuration(exec.executionTimeMs)],
      ['Rows', formatNumber(exec.rowCount ?? 0)],
      ['File size', formatBytes(exec.fileSizeBytes)],
      ['Connection', connection ? `${connection.name} (${connection.type})` : null],
      ['Destination', destination ? `${destination.name} (${destination.type})` : null],
    ]}
  />

  {#if exec.outputPath}
    <section class="block">
      <h4>Output file</h4>
      {#if token}<a href="/api/executions/download/{token}" download>{exec.outputPath.split('/').pop()}</a>{:else}<span
          >{exec.outputPath.split('/').pop()}</span
        >{/if}
    </section>
  {/if}

  {#each [['Pre-processing', 'preProcess'], ['Post-processing', 'postProcess']] as [title, p]}
    {#if exec[`${p}Status`]}
      <section class="block">
        <h4>
          {title}
          <StatusMark kind={statusKind(exec[`${p}Status`])} label={statusLabel(exec[`${p}Status`])} />
          <span class="num muted">{formatDuration(exec[`${p}TimeMs`])}</span>
        </h4>
        {#if exec[`${p}Error`]}<pre class="code err">{exec[`${p}Error`].trim()}</pre>{/if}
        <ScriptOutput title="Script output" exitCode={exec[`${p}ExitCode`]} stdout={exec[`${p}Stdout`]} stderr={exec[`${p}Stderr`]} />
      </section>
    {/if}
  {/each}

  {#if exec.errorMessage}
    <section class="block">
      <h4>
        Error <button type="button" class="btn ghost icon" aria-label="Copy error" onclick={() => copyText(exec.errorMessage)}
          ><Copy size={13} /></button
        >
      </h4>
      <pre class="code err">{exec.errorMessage.trim()}</pre>
    </section>
  {/if}
  {#if exec.outputMessage}
    <section class="block">
      <h4>
        Response <button type="button" class="btn ghost icon" aria-label="Copy response" onclick={() => copyText(exec.outputMessage)}
          ><Copy size={13} /></button
        >
      </h4>
      <pre class="code">{exec.outputMessage.trim()}</pre>
    </section>
  {/if}

  {#if exec.wasSplit && splits.length}
    <section class="block">
      <h4>Split files <span class="num muted">{exec.splitSuccessCount ?? 0} of {exec.splitCount ?? 0} succeeded</span></h4>
      <table class="data">
        <thead><tr><th>Key</th><th>Rows</th><th>Size</th><th>Status</th></tr></thead>
        <tbody>
          {#each splits as s}
            <tr>
              <td
                >{s.splitKey}{#if s.errorMessage}<div class="err small">{s.errorMessage}</div>{/if}</td
              >
              <td class="num">{formatNumber(s.rowCount)}</td>
              <td class="num">{formatBytes(s.fileSizeBytes)}</td>
              <td><StatusMark kind={statusKind(s.status)} label={statusLabel(s.status)} /></td>
            </tr>
          {/each}
        </tbody>
      </table>
    </section>
  {/if}

  {#if failures.length}
    <section class="block">
      <h4>Send failures <span class="num muted">{failures.length}</span></h4>
      <ul>
        {#each failures as f}<li><strong>{f.detail ?? f.errorType}</strong>: {f.errorMessage}</li>{/each}
      </ul>
    </section>
  {/if}

  <div><button type="button" class="btn" onclick={downloadLog}><Download size={14} />Download log</button></div>
{/if}

<style>
  .head {
    display: flex;
    justify-content: space-between;
    gap: 16px;
    align-items: flex-start;
  }
  .block {
    display: flex;
    flex-direction: column;
    gap: 6px;
  }
  h4 {
    display: flex;
    align-items: center;
    gap: 8px;
  }
  .err {
    color: var(--err);
  }
  .small {
    font-size: 12px;
  }
  ul {
    margin: 0;
    padding-left: 18px;
  }
</style>
