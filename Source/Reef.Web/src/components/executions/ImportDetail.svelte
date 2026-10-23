<script lang="ts">
  import StatusMark from '../StatusMark.svelte';
  import Facts from './Facts.svelte';
  import ScriptOutput from './ScriptOutput.svelte';
  import { api } from '../../lib/api';
  import { formatBytes, formatDateTime, formatDuration, formatNumber, parseDate } from '../../lib/format';
  import { notify } from '../../lib/notify';
  import { statusKind, statusLabel } from '../../lib/status';
  import type { ImportExecution } from '../../models/ImportExecution';
  import type { ImportExecutionError } from '../../models/ImportExecutionError';
  import type { ImportProfile } from '../../models/ImportProfile';
  import type { ImportTargetResult } from '../../models/ImportTargetResult';

  let { id, profiles }: { id: number; profiles: ImportProfile[] } = $props();

  let exec = $state<ImportExecution | null>(null);
  let errors = $state<ImportExecutionError[]>([]);

  $effect(() => {
    load(id);
  });

  async function load(executionId: number) {
    exec = null;
    errors = [];
    try {
      [exec, errors] = await Promise.all([
        api.get<ImportExecution>(`/api/import-profiles/executions/${executionId}`),
        api.get<ImportExecutionError[]>(`/api/import-profiles/executions/${executionId}/errors`).catch(() => []),
      ]);
    } catch (err) {
      notify.fail(err);
    }
  }

  function parse<T>(json: string | null | undefined, fallback: T): T {
    try {
      return json ? JSON.parse(json) : fallback;
    } catch {
      return fallback;
    }
  }

  const durationMs = $derived(exec?.completedAt ? parseDate(exec.completedAt)!.getTime() - parseDate(exec.startedAt)!.getTime() : null);
  const phases = $derived(Object.entries(parse<Record<string, number>>(exec?.phaseTimingsJson, {})));
  const maxPhase = $derived(Math.max(1, ...phases.map(([, ms]) => ms)));
  const targets = $derived(parse<ImportTargetResult[]>(exec?.outputTargetsJson, []));
  const name = $derived.by(() => {
    const profileId = exec?.importProfileId;
    return profileId === undefined ? '' : (profiles.find((p) => p.id === profileId)?.name ?? `Profile ${profileId}`);
  });
</script>

{#if !exec}
  <p class="muted">Loading execution</p>
{:else}
  <div class="head">
    <div>
      <h3>{name}</h3>
      <p class="muted">Import execution <span class="num">{exec.id}</span> · Triggered by {exec.triggeredBy || 'System'}</p>
    </div>
    <StatusMark kind={statusKind(exec.status)} label={statusLabel(exec.status)} />
  </div>

  <Facts
    items={[
      ['Started', formatDateTime(exec.startedAt)],
      ['Duration', durationMs !== null ? formatDuration(durationMs) : 'Running'],
      [
        'Throughput',
        durationMs && exec.totalRowsRead > 0 ? `${formatNumber(Math.round(exec.totalRowsRead / (durationMs / 1000)))} rows/s` : null,
      ],
      ['Files', exec.filesProcessed],
      ['Bytes', formatBytes(exec.bytesProcessed)],
    ]}
  />

  <Facts
    items={[
      ['Read', exec.totalRowsRead ?? 0],
      ['Inserted', exec.rowsInserted ?? 0],
      ['Updated', exec.rowsUpdated ?? 0],
      ['Skipped', exec.rowsSkipped ?? 0],
      ['Deleted', exec.rowsDeleted ?? 0],
      ['Failed', exec.rowsFailed ?? 0],
    ]}
  />

  {#if phases.length}
    <section class="block">
      <h4>Phases</h4>
      {#each phases as [phase, ms] (phase)}
        <div class="phase">
          <span>{phase}</span><span class="bar"><span style:width="{Math.round((ms / maxPhase) * 100)}%"></span></span><span class="num"
            >{formatDuration(ms)}</span
          >
        </div>
      {/each}
    </section>
  {/if}

  {#if exec.deltaSyncNewRows || exec.deltaSyncChangedRows || exec.deltaSyncUnchangedRows || exec.deltaSyncDeletedRows}
    <section class="block">
      <h4>Smart Sync</h4>
      <Facts
        items={[
          ['New', exec.deltaSyncNewRows ?? 0],
          ['Changed', exec.deltaSyncChangedRows ?? 0],
          ['Unchanged', exec.deltaSyncUnchangedRows ?? 0],
          ['Deleted', exec.deltaSyncDeletedRows ?? 0],
        ]}
      />
    </section>
  {/if}

  {#if exec.errorMessage}
    <section class="block">
      <h4>Error</h4>
      <pre class="code err">{exec.errorMessage}</pre>
      {#if exec.stackTrace}<details>
          <summary>Stack trace</summary>
          <pre class="code">{exec.stackTrace}</pre>
        </details>{/if}
    </section>
  {/if}

  <ScriptOutput
    title="Pre-process script"
    exitCode={exec.preProcessExitCode}
    stdout={exec.preProcessStdout}
    stderr={exec.preProcessStderr}
  />
  <ScriptOutput
    title="Post-process script"
    exitCode={exec.postProcessExitCode}
    stdout={exec.postProcessStdout}
    stderr={exec.postProcessStderr}
  />

  {#if targets.length}
    <section class="block">
      <h4>Output targets</h4>
      <table class="data">
        <thead><tr><th>Destination</th><th>Rows</th><th>Status</th></tr></thead>
        <tbody>
          {#each targets as t, i (i)}
            <tr>
              <td
                >{t.destinationName ?? `Destination ${t.destinationId}`}{#if t.finalPath}<div class="muted small">
                    {t.finalPath}
                  </div>{/if}{#if t.errorMessage}<div class="err small">{t.errorMessage}</div>{/if}</td
              >
              <td class="num">{formatNumber(t.rowsWritten)}</td>
              <td><StatusMark kind={statusKind(t.status)} label={statusLabel(t.status)} /></td>
            </tr>
          {/each}
        </tbody>
      </table>
    </section>
  {/if}

  {#if errors.length}
    <section class="block">
      <h4>Row errors <span class="num muted">{errors.length}</span></h4>
      <table class="data">
        <thead><tr><th>Row</th><th>Phase</th><th>Error</th></tr></thead>
        <tbody>
          {#each errors as e (e.id)}
            <tr>
              <td class="num">{e.rowNumber ?? ''}</td>
              <td>{e.phase ?? e.errorType}</td>
              <td
                >{e.errorMessage}{#if e.rowDataJson}<div class="muted small trunc">{e.rowDataJson.substring(0, 160)}</div>{/if}</td
              >
            </tr>
          {/each}
        </tbody>
      </table>
    </section>
  {/if}
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
  .err {
    color: var(--err);
  }
  .small {
    font-size: 12px;
  }
  .trunc {
    max-width: 360px;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  .phase {
    display: grid;
    grid-template-columns: 140px minmax(0, 1fr) 70px;
    align-items: center;
    gap: 12px;
  }
  .phase .num {
    text-align: right;
  }
  .bar {
    height: 8px;
    background: var(--hover);
    border-radius: 2px;
    overflow: hidden;
  }
  .bar span {
    display: block;
    height: 100%;
    background: var(--accent);
  }
  summary {
    cursor: pointer;
    color: var(--muted);
    font-size: 12px;
  }
</style>
