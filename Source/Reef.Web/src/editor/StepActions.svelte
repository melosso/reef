<script lang="ts">
  import { Link, MailOpen, Plug, RotateCcw, Table, Terminal } from '@lucide/svelte';
  import { api } from '../lib/api';
  import { confirm } from '../lib/confirm.svelte';
  import { copyText, notify } from '../lib/notify';
  import { tooltip } from '../lib/tooltip';
  import { textOf } from './config';

  let {
    kind,
    id,
    step,
    config,
    dirty,
    flatten,
  }: {
    kind: 'export' | 'import';
    id: number | null | undefined;
    step: string;
    config: Record<string, any>;
    dirty: boolean;
    flatten: () => Promise<any>;
  } = $props();

  const key = $derived(`${kind}:${step}`);
  let busy = $state<string | null>(null);
  let note = $state('');
  let table = $state<{ columns: string[]; rows: Record<string, any>[] } | null>(null);
  let output = $state('');
  let html = $state('');
  let syncText = $state('');
  let hooks = $state<any[]>([]);
  let revealed = $state('');

  const owner = $derived(kind === 'import' ? 'importProfileId' : 'profileId');
  const base = $derived(kind === 'import' ? `/api/import-profiles/${id}` : `/api/profiles/${id}`);
  const scriptPrefix = $derived(step === 'preprocess' ? 'PreProcess' : 'PostProcess');

  $effect(() => {
    key;
    note = '';
    table = null;
    output = '';
    html = '';
    revealed = '';
    if (!id) return;
    if (step === 'deltasync') loadSync();
    if (step === 'settings') loadHooks();
  });

  async function act(name: string, fn: () => Promise<void>) {
    busy = name;
    try {
      await fn();
    } catch (e) {
      notify.fail(e, `step-${name}`);
    } finally {
      busy = null;
    }
  }

  const previewRows = () =>
    act('preview', async () => {
      const result = await api.post('/api/profiles/test-query-preview', {
        connectionId: config.ConnectionId || 0,
        query: config.Query || '',
      });
      if (!result.success) throw new Error(result.error || 'Query failed.');
      note = `${result.rowCount} rows in ${result.totalExecutionTimeMs} ms. Showing up to 25.`;
      table = { columns: result.columns, rows: result.rows };
    });

  const runScript = () =>
    act('script', async () => {
      const cfg = config[`${scriptPrefix}Config`] || {};
      const result = await api.post('/api/profiles/test-script', {
        profileId: id || null,
        interpreter: cfg.Interpreter || '',
        scriptPathOrInline: cfg.ScriptPathOrInline || '',
        scriptIsInline: cfg.ScriptIsInline !== false,
        timeout: cfg.Timeout || 30,
        envAllowlist: cfg.EnvAllowlist || null,
      });
      note = `Exit code ${result.exitCode} in ${result.elapsedMs} ms`;
      output = [result.stdout, result.stderr, result.errorMessage].filter(Boolean).join('\n') || 'No output';
      if (!result.success) notify.error(result.errorMessage || `Script exited with code ${result.exitCode}.`);
    });

  async function loadSync() {
    try {
      const stats = await api.get(`${base}/delta-sync/stats`);
      syncText = !stats.enabled
        ? stats.message || 'Smart Sync is not saved on this profile.'
        : `${stats.activeRows} tracked, ${stats.deletedRows} deleted. Last run: ${stats.newRowsLastRun ?? 0} new, ${stats.changedRowsLastRun ?? 0} changed, ${stats.unchangedRowsLastRun ?? 0} unchanged.`;
    } catch {
      syncText = 'Sync state is unavailable.';
    }
  }

  const resetSync = () =>
    act('reset', async () => {
      if (
        !(await confirm({
          title: 'Reset Smart Sync?',
          message: 'The next run treats every row as new.',
          confirm: 'Reset sync state',
          danger: true,
        }))
      )
        return;
      if (kind === 'import') await api.del(`${base}/delta-sync/reset`);
      else await api.post(`${base}/delta-sync/reset`);
      notify.success('Smart Sync state reset');
      await loadSync();
    });

  const previewEmail = () =>
    act('email', async () => {
      const result = await api.post(`/api/profiles/${id}/preview-email-html`);
      if (!result.success) throw new Error(result.message || 'Preview failed.');
      note = result.message || '';
      html = result.html || '';
    });

  const checkSource = () =>
    act('source', async () => {
      const result = await api.post('/api/import-profiles/test-source', await flatten());
      (result.success ? notify.success : notify.error)(
        result.message || (result.success ? 'Source is reachable.' : 'Source check failed.'),
      );
    });

  const previewRecords = () =>
    act('records', async () => {
      const profile = await flatten();
      const result = await api.post('/api/import-profiles/preview', { ...profile, importProfileId: null, maxRows: 10 });
      if (!result.success) throw new Error(result.errorMessage || 'Preview failed.');
      note = `${result.totalRowsParsed} records read. Showing ${result.rowsReturned}.`;
      table = { columns: result.columns, rows: result.rows };
      (result.parseWarnings || []).slice(0, 3).forEach((w: string) => notify.warning(w));
    });

  const checkTarget = () =>
    act('target', async () => {
      const profile = await flatten();
      const result = await api.post('/api/import-profiles/test-target', profile);
      (result.success ? notify.success : notify.error)(
        result.message || (result.success ? 'Target is reachable.' : 'Target check failed.'),
      );
      if (!result.success || config.TargetType === 'LocalFile') return;
      const schema = await api.post('/api/import-profiles/target-schema', profile);
      const columns = Array.isArray(schema) ? schema : schema.columns || [];
      note = columns.length ? `Columns: ${columns.map((c: any) => c.columnName || c.name).join(', ')}` : 'No columns found.';
    });

  async function loadHooks() {
    hooks = await api.get<any[]>(`/api/webhooks?${owner}=${id}`).catch(() => []);
  }

  function reveal(url: string) {
    revealed = url;
    copyText(url);
  }

  const addHook = () =>
    act('hook', async () => {
      const result = await api.post('/api/webhooks', { [owner]: id });
      reveal(result.url);
      await loadHooks();
    });

  const hookAction = (hook: any, action: 'toggle' | 'regenerate' | 'delete') =>
    act(`hook-${hook.id}`, async () => {
      if (action === 'toggle') await api.post(`/api/webhooks/${hook.id}/${hook.isActive ? 'disable' : 'enable'}`);
      if (action === 'regenerate') {
        if (
          !(await confirm({
            title: 'Create a new token?',
            message: 'The current webhook URL stops working.',
            confirm: 'Create new token',
            danger: true,
          }))
        )
          return;
        reveal((await api.post(`/api/webhooks/${hook.id}/regenerate`)).url);
      }
      if (action === 'delete') {
        if (
          !(await confirm({
            title: 'Delete webhook?',
            message: 'Calls to this webhook URL fail after deletion.',
            confirm: 'Delete webhook',
            danger: true,
          }))
        )
          return;
        await api.del(`/api/webhooks/${hook.id}`);
      }
      await loadHooks();
    });

  const needsSave = $derived(['settings', 'deltasync', 'emailexport'].includes(step) && !id);
  const hasActions = $derived(
    [
      'export:settings',
      'import:settings',
      'export:source',
      'export:preprocess',
      'export:postprocess',
      'import:preprocess',
      'import:postprocess',
      'export:deltasync',
      'import:deltasync',
      'export:emailexport',
      'import:source',
      'import:parse',
      'import:target',
    ].includes(key) && !((step === 'preprocess' || step === 'postprocess') && config[`${scriptPrefix}Type`] !== 'Script'),
  );
</script>

{#if hasActions}
  <section class="actions">
    {#if needsSave}
      <p class="muted">Save the profile to use {step === 'settings' ? 'webhooks' : step === 'deltasync' ? 'sync state' : 'the preview'}.</p>
    {:else if key === 'export:source'}
      <button type="button" class="btn" aria-busy={busy === 'preview'} onclick={previewRows}><Table size={14} />Preview rows</button>
    {:else if step === 'preprocess' || step === 'postprocess'}
      <button
        type="button"
        class="btn"
        aria-busy={busy === 'script'}
        use:tooltip={'Runs the script once. The profile is not saved.'}
        onclick={runScript}><Terminal size={14} />Run script</button
      >
    {:else if step === 'deltasync'}
      <p>{syncText}</p>
      <button type="button" class="btn" aria-busy={busy === 'reset'} use:tooltip={'Forget all tracked rows'} onclick={resetSync}
        ><RotateCcw size={14} />Reset sync state</button
      >
    {:else if key === 'export:emailexport'}
      <button
        type="button"
        class="btn"
        aria-busy={busy === 'email'}
        disabled={dirty}
        use:tooltip={dirty ? 'Save the profile to preview the saved version' : 'Renders the first email of the saved profile'}
        onclick={previewEmail}><MailOpen size={14} />Preview first email</button
      >
    {:else if key === 'import:source'}
      <button type="button" class="btn" aria-busy={busy === 'source'} onclick={checkSource}><Plug size={14} />Check source</button>
    {:else if key === 'import:parse'}
      <button
        type="button"
        class="btn"
        aria-busy={busy === 'records'}
        use:tooltip={'Reads the source with the current settings'}
        onclick={previewRecords}><Table size={14} />Preview records</button
      >
    {:else if key === 'import:target'}
      <button type="button" class="btn" aria-busy={busy === 'target'} onclick={checkTarget}><Plug size={14} />Check target</button>
    {:else if step === 'settings'}
      <h4>Webhooks</h4>
      {#each hooks as hook (hook.id)}
        <div class="hook">
          <span class="num token">{hook.token}</span>
          <span class="muted">{hook.isActive ? 'On' : 'Off'}, <span class="num">{hook.triggerCount}</span> calls</span>
          <span class="links">
            <button type="button" class="link" onclick={() => hookAction(hook, 'toggle')}>{hook.isActive ? 'Turn off' : 'Turn on'}</button>
            <button type="button" class="link" onclick={() => hookAction(hook, 'regenerate')}>New token</button>
            <button type="button" class="link danger" onclick={() => hookAction(hook, 'delete')}>Delete</button>
          </span>
        </div>
      {:else}
        <p class="muted">No webhooks</p>
      {/each}
      <button
        type="button"
        class="btn"
        aria-busy={busy === 'hook'}
        use:tooltip={'Runs this profile on an HTTP POST to the webhook URL'}
        onclick={addHook}><Link size={14} />Add webhook</button
      >
      {#if revealed}
        <div class="field">
          <label for="webhook-url">Webhook URL. The token is displayed once.</label>
          <input id="webhook-url" readonly value={revealed} onfocus={(e) => e.currentTarget.select()} />
        </div>
      {/if}
    {/if}

    {#if note}<p class="muted">{note}</p>{/if}
    {#if output}<pre class="code">{output}</pre>{/if}
    {#if html}<iframe class="frame" sandbox="" title="Email preview" srcdoc={html}></iframe>{/if}
    {#if table}
      <div class="preview">
        <table class="data">
          <thead
            ><tr
              >{#each table.columns as c}<th>{c}</th>{/each}</tr
            ></thead
          >
          <tbody
            >{#each table.rows as row}<tr
                >{#each table.columns as c}<td>{textOf(row[c])}</td>{/each}</tr
              >{/each}</tbody
          >
        </table>
      </div>
    {/if}
  </section>
{/if}

<style>
  .actions {
    display: flex;
    flex-direction: column;
    gap: 12px;
    align-items: flex-start;
  }
  .actions > * {
    max-width: 100%;
  }
  .hook {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: 6px 12px;
    width: 100%;
    padding: 8px 0;
    border-bottom: 1px solid var(--line);
  }
  .token {
    flex: 1;
    min-width: 0;
    overflow: hidden;
    text-overflow: ellipsis;
  }
  .links {
    display: flex;
    gap: 10px;
  }
  .danger {
    color: var(--err);
  }
  .field {
    width: 100%;
  }
  .frame {
    width: 100%;
    height: 420px;
    border: 1px solid var(--line);
    border-radius: var(--radius);
    background: #ffffff;
  }
  .preview {
    width: 100%;
    max-height: 360px;
    overflow: auto;
    border: 1px solid var(--line);
    border-radius: var(--radius);
  }
  .preview td {
    font-size: 12px;
    white-space: nowrap;
  }
  pre.code {
    width: 100%;
  }
</style>
