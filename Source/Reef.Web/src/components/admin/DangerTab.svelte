<script lang="ts">
  import { api } from '../../lib/api';
  import type { ConfirmationString } from '../../models/ConfirmationString';
  import type { MessageResult } from '../../models/MessageResult';
  import { confirm } from '../../lib/confirm.svelte';
  import { notify } from '../../lib/notify';
  import { navigate } from '../../lib/router.svelte';
  import { clearSession } from '../../lib/session.svelte';

  let auditDays = $state(90);
  let executionDays = $state(30);

  async function retention(kind: 'audits' | 'executions') {
    const days = kind === 'audits' ? auditDays : executionDays;
    if (
      !(await confirm({
        title: `Delete ${kind === 'audits' ? 'audit entries' : 'executions'} older than ${days} days?`,
        message: 'Deleted records cannot be restored.',
        confirm: 'Delete records',
        danger: true,
      }))
    )
      return;
    try {
      const r = await api.post<MessageResult>(`/api/admin/danger/clear-old-${kind}`, { retentionDays: Number(days) });
      notify.success(r?.message ?? 'Records deleted');
    } catch (e) {
      notify.fail(e);
    }
  }

  async function typed(kind: 'clear-all-executions' | 'reset-database') {
    try {
      const { confirmationString } = await api.get<ConfirmationString>('/api/admin/danger/confirmation-string');
      const reset = kind === 'reset-database';
      if (
        !(await confirm({
          title: reset ? 'Reset the database?' : 'Delete all executions?',
          message: reset
            ? 'All profiles, connections, destinations, jobs, users and history are deleted.'
            : 'The full execution history is deleted.',
          confirm: reset ? 'Reset database' : 'Delete all executions',
          danger: true,
          typed: confirmationString,
        }))
      )
        return;
      const r = await api.post<MessageResult>(`/api/admin/danger/${kind}`, { confirmationString });
      notify.success(r?.message ?? (reset ? 'Database reset' : 'Executions deleted'));
      if (reset)
        setTimeout(() => {
          clearSession();
          navigate('/logoff');
        }, 1500);
    } catch (e) {
      notify.fail(e);
    }
  }
</script>

<div class="fields narrow">
  <section class="panel">
    <div class="panel-head"><h3>Delete old audit entries</h3></div>
    <div class="panel-body row">
      <div class="field">
        <label for="dz-audit">Keep entries from the last days</label><input id="dz-audit" type="number" min="90" bind:value={auditDays} />
      </div>
      <button type="button" class="btn danger" onclick={() => retention('audits')}>Delete older entries</button>
    </div>
  </section>
  <section class="panel">
    <div class="panel-head"><h3>Delete old executions</h3></div>
    <div class="panel-body row">
      <div class="field">
        <label for="dz-exec">Keep executions from the last days</label><input
          id="dz-exec"
          type="number"
          min="1"
          bind:value={executionDays}
        />
      </div>
      <button type="button" class="btn danger" onclick={() => retention('executions')}>Delete older executions</button>
    </div>
  </section>
  <section class="panel">
    <div class="panel-head"><h3>Delete all executions</h3></div>
    <div class="panel-body row">
      <p class="muted">Deletes the complete execution history.</p>
      <button type="button" class="btn danger" onclick={() => typed('clear-all-executions')}>Delete all executions</button>
    </div>
  </section>
  <section class="panel danger-panel">
    <div class="panel-head"><h3>Reset database</h3></div>
    <div class="panel-body row">
      <p class="muted">Deletes every record and restores the initial state.</p>
      <button type="button" class="btn danger" onclick={() => typed('reset-database')}>Reset database</button>
    </div>
  </section>
</div>

<style>
  .narrow {
    max-width: 720px;
  }
  .row {
    display: flex;
    align-items: flex-end;
    gap: 16px;
    justify-content: space-between;
  }
  .row .field {
    flex: 1;
    max-width: 260px;
  }
  .row p {
    flex: 1;
    align-self: center;
  }
  .danger-panel {
    border-color: var(--err);
  }
</style>
