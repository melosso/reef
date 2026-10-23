<script lang="ts">
  import { Copy, Download } from '@lucide/svelte';
  import StatusMark from '../StatusMark.svelte';
  import Facts from './Facts.svelte';
  import ScriptOutput from './ScriptOutput.svelte';
  import { api } from '../../lib/api';
  import { formatBytes, formatDateTime, formatDuration, formatNumber } from '../../lib/format';
  import { copyText, notify } from '../../lib/notify';
  import { statusKind, statusLabel } from '../../lib/status';
  import type { Connection } from '../../models/Connection';
  import type { Destination } from '../../models/Destination';
  import type { ExecutionError } from '../../models/ExecutionError';
  import type { ExecutionSplit } from '../../models/ExecutionSplit';
  import type { ExportExecution } from '../../models/ExportExecution';
  import type { ExportProfile } from '../../models/ExportProfile';
  import type { Job } from '../../models/Job';

  let { id, profiles, jobs }: { id: number; profiles: ExportProfile[]; jobs: Job[] } = $props();

  let exec = $state<ExportExecution | null>(null);
  let connection = $state<Connection | null>(null);
  let destination = $state<Destination | null>(null);
  let splits = $state<ExecutionSplit[]>([]);
  let failures = $state<ExecutionError[]>([]);

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
      const e = await api.get<ExportExecution>(`/api/executions/${executionId}`);
      exec = e;
      const profile = profiles.find((p) => p.id === e.profileId);
      if (profile?.connectionId) connection = await api.get<Connection>(`/api/connections/${profile.connectionId}`).catch(() => null);
      if (profile?.outputDestinationId)
        destination = await api.get<Destination>(`/api/destinations/${profile.outputDestinationId}`).catch(() => null);
      if (e.wasSplit) splits = await api.get<ExecutionSplit[]>(`/api/profiles/executions/${executionId}/splits`).catch(() => []);
      if ((e.splitFailureCount ?? 0) > 0)
        failures = await api.get<ExecutionError[]>(`/api/executions/${executionId}/errors`).catch(() => []);
    } catch (err) {
      notify.fail(err);
    }
  }

  const profileName = $derived.by(() => {
    if (!exec) return '';
    const { profileId } = exec;
    return profiles.find((p) => p.id === profileId)?.name ?? exec.profileName ?? `Profile ${profileId}`;
  });
  const jobName = $derived.by(() => {
    const jobId = exec?.jobId;
    return jobId ? (jobs.find((j) => j.id === jobId)?.name ?? `Job ${jobId}`) : null;
  });
  const processes = $derived(
    exec
      ? [
          {
            title: 'Pre-processing',
            status: exec.preProcessStatus,
            timeMs: exec.preProcessTimeMs,
            error: exec.preProcessError,
            exitCode: exec.preProcessExitCode,
            stdout: exec.preProcessStdout,
            stderr: exec.preProcessStderr,
          },
          {
            title: 'Post-processing',
            status: exec.postProcessStatus,
            timeMs: exec.postProcessTimeMs,
            error: exec.postProcessError,
            exitCode: exec.postProcessExitCode,
            stdout: exec.postProcessStdout,
            stderr: exec.postProcessStderr,
          },
        ]
      : [],
  );
  const token = $derived(exec?.outputPath ? /exports\/[\d-]+\/([^/]+)\//.exec(exec.outputPath)?.[1] : null);

  function downloadLog() {
    if (!exec) return;
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

  {#each processes as process (process.title)}
    {#if process.status}
      <section class="block">
        <h4>
          {process.title}
          <StatusMark kind={statusKind(process.status)} label={statusLabel(process.status)} />
          <span class="num muted">{formatDuration(process.timeMs)}</span>
        </h4>
        {#if process.error}<pre class="code err">{process.error.trim()}</pre>{/if}
        <ScriptOutput title="Script output" exitCode={process.exitCode} stdout={process.stdout} stderr={process.stderr} />
      </section>
    {/if}
  {/each}

  {#if exec.errorMessage}
    {@const message = exec.errorMessage}
    <section class="block">
      <h4>
        Error <button type="button" class="btn ghost icon" aria-label="Copy error" onclick={() => copyText(message)}
          ><Copy size={13} /></button
        >
      </h4>
      <pre class="code err">{exec.errorMessage.trim()}</pre>
    </section>
  {/if}
  {#if exec.outputMessage}
    {@const response = exec.outputMessage}
    <section class="block">
      <h4>
        Response <button type="button" class="btn ghost icon" aria-label="Copy response" onclick={() => copyText(response)}
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
          {#each splits as s (s.id)}
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
        {#each failures as f (f.id)}<li><strong>{f.detail ?? f.errorType}</strong>: {f.errorMessage}</li>{/each}
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
