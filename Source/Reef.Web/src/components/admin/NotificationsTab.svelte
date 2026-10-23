<script lang="ts">
  import { api } from '../../lib/api';
  import type { CheckResult } from '../../models/CheckResult';
  import type { Destination } from '../../models/Destination';
  import type { NotificationSettings } from '../../models/NotificationSettings';
  import { notify } from '../../lib/notify';
  import { tooltip } from '../../lib/tooltip';

  type Toggle = { [K in keyof NotificationSettings]-?: NotificationSettings[K] extends boolean ? K : never }[keyof NotificationSettings];

  const EVENTS: [Toggle, string][] = [
    ['notifyOnProfileFailure', 'Export profile failed'],
    ['notifyOnProfileSuccess', 'Export profile succeeded'],
    ['notifyOnImportProfileFailure', 'Import profile failed'],
    ['notifyOnImportProfileSuccess', 'Import profile succeeded'],
    ['notifyOnJobFailure', 'Job failed'],
    ['notifyOnJobSuccess', 'Job succeeded'],
    ['notifyOnNewApiKey', 'API key created'],
    ['notifyOnNewUser', 'User created'],
    ['notifyOnNewWebhook', 'Webhook created'],
  ];

  let s = $state<NotificationSettings | null>(null);
  let destinations = $state<Destination[]>([]);
  let thresholdMb = $state(1024);
  let busy = $state<string | null>(null);

  $effect(() => {
    Promise.all([
      api.get<NotificationSettings>('/api/admin/notifications').catch(() => null),
      api.get<Destination[]>('/api/destinations?type=Email').catch(() => []),
    ]).then(([settings, dests]) => {
      destinations = (dests ?? []).filter((d) => d.type === 'Email');
      s = {
        isEnabled: false,
        destinationId: null,
        recipientEmails: '',
        notifyOnProfileFailure: true,
        notifyOnProfileSuccess: false,
        notifyOnImportProfileFailure: true,
        notifyOnImportProfileSuccess: false,
        notifyOnJobFailure: true,
        notifyOnJobSuccess: false,
        notifyOnDatabaseSizeThreshold: true,
        notifyOnNewApiKey: true,
        notifyOnNewUser: false,
        notifyOnNewWebhook: false,
        notifyOnNewEmailApproval: false,
        newEmailApprovalCooldownHours: 24,
        enableCTA: false,
        ctaUrl: '',
        ...(settings ?? {}),
      };
      thresholdMb = settings?.databaseSizeThresholdBytes ? Math.round(settings.databaseSizeThresholdBytes / 1048576) : 1024;
    });
  });

  async function save(e: SubmitEvent) {
    e.preventDefault();
    if (!s) return;
    busy = 'save';
    try {
      const destinationId = Number(s.destinationId);
      const dest = destinations.find((d) => d.id === destinationId);
      await api.post('/api/admin/notifications', {
        ...s,
        id: s.id ?? 0,
        destinationId: s.destinationId ? Number(s.destinationId) : null,
        destinationName: dest?.name ?? null,
        recipientEmails: s.recipientEmails?.trim() || null,
        ctaUrl: s.ctaUrl?.trim() || null,
        databaseSizeThresholdBytes: Number(thresholdMb) * 1048576,
        newEmailApprovalCooldownHours: Number(s.newEmailApprovalCooldownHours),
        createdAt: s.createdAt ?? new Date().toISOString(),
        updatedAt: new Date().toISOString(),
      });
      notify.success('Notification settings saved', { key: 'notif-save' });
    } catch (err) {
      notify.fail(err, 'notif-save');
    } finally {
      busy = null;
    }
  }

  async function test() {
    if (!s) return;
    busy = 'test';
    try {
      const r = await api.post<CheckResult>('/api/admin/notifications/test', {
        destinationId: s.destinationId ? Number(s.destinationId) : null,
        recipientEmails: s.recipientEmails?.trim() || null,
      });
      if (r?.success === false) throw new Error(r.message || 'Test email failed.');
      notify.success(r?.message || 'Test email sent', { key: 'notif-test' });
    } catch (err) {
      notify.fail(err, 'notif-test');
    } finally {
      busy = null;
    }
  }
</script>

{#if s}
  <form class="fields narrow" onsubmit={save}>
    <label class="check"><input type="checkbox" role="switch" bind:checked={s.isEnabled} />Send email notifications</label>
    <div class="field">
      <label for="n-dest">Email destination</label>
      <select id="n-dest" bind:value={s.destinationId}>
        <option value={null}>Choose</option>
        {#each destinations as d (d.id)}<option value={d.id}>{d.name}</option>{/each}
      </select>
    </div>
    <div class="field">
      <label for="n-to" use:tooltip={'Comma-separated email addresses'}>Recipients</label><input
        id="n-to"
        bind:value={s.recipientEmails}
        placeholder="ops@example.com"
      />
    </div>

    <fieldset>
      <legend>Events</legend>
      <div class="grid2">
        {#each EVENTS as [key, label] (key)}<label class="check"><input type="checkbox" bind:checked={s[key]} />{label}</label>{/each}
      </div>
    </fieldset>

    <fieldset>
      <legend>Database size</legend>
      <label class="check"><input type="checkbox" bind:checked={s.notifyOnDatabaseSizeThreshold} />Alert above the threshold</label>
      {#if s.notifyOnDatabaseSizeThreshold}<div class="field">
          <label for="n-db">Threshold in MB</label><input id="n-db" type="number" min="1" bind:value={thresholdMb} />
        </div>{/if}
    </fieldset>

    <fieldset>
      <legend>Email approvals</legend>
      <label class="check"><input type="checkbox" bind:checked={s.notifyOnNewEmailApproval} />Alert on new pending approvals</label>
      {#if s.notifyOnNewEmailApproval}<div class="field">
          <label for="n-cool" use:tooltip={'Minimum time between two approval alerts'}>Cooldown in hours</label><input
            id="n-cool"
            type="number"
            min="1"
            bind:value={s.newEmailApprovalCooldownHours}
          />
        </div>{/if}
    </fieldset>

    <fieldset>
      <legend>Button in emails</legend>
      <label class="check"><input type="checkbox" bind:checked={s.enableCTA} />Add a link button to Reef</label>
      {#if s.enableCTA}<div class="field">
          <label for="n-cta">Reef URL</label><input id="n-cta" type="url" placeholder="https://reef.example.com" bind:value={s.ctaUrl} />
        </div>{/if}
    </fieldset>

    <div class="row">
      <button type="submit" class="btn primary" aria-busy={busy === 'save'}>Save settings</button>
      <button type="button" class="btn" aria-busy={busy === 'test'} onclick={test}>Send test email</button>
    </div>
  </form>
{/if}

<style>
  .narrow {
    max-width: 640px;
  }
  fieldset {
    border: 1px solid var(--line);
    border-radius: var(--radius);
    padding: 12px 14px;
    margin: 0;
    display: flex;
    flex-direction: column;
    gap: 12px;
  }
  legend {
    font-size: 13px;
    font-weight: 500;
    color: var(--soft);
    padding: 0 4px;
  }
  .row {
    display: flex;
    gap: 8px;
  }
</style>
