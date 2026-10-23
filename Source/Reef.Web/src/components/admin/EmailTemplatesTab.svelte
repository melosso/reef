<script lang="ts">
  import CodeEditor from '../../editor/CodeEditor.svelte';
  import { api } from '../../lib/api';
  import { confirm } from '../../lib/confirm.svelte';
  import { notify } from '../../lib/notify';
  import { tooltip } from '../../lib/tooltip';

  const TYPES: [string, string][] = [
    ['ProfileSuccess', 'Profile succeeded'],
    ['ProfileFailure', 'Profile failed'],
    ['JobSuccess', 'Job succeeded'],
    ['JobFailure', 'Job failed'],
    ['NewUser', 'User created'],
    ['NewApiKey', 'API key created'],
    ['NewWebhook', 'Webhook created'],
    ['NewEmailApproval', 'New email approval'],
    ['DatabaseSizeThreshold', 'Database size alert'],
  ];

  let type = $state('');
  let t = $state<any>(null);
  let showPreview = $state(false);
  let busy = $state<string | null>(null);

  async function load(kind: string) {
    t = null;
    showPreview = false;
    if (!kind) return;
    try {
      t = await api.get(`/api/admin/notification-templates/${kind}`);
    } catch (e) {
      notify.fail(e);
    }
  }

  $effect(() => {
    load(type);
  });

  async function save() {
    busy = 'save';
    try {
      await api.put(`/api/admin/notification-templates/${type}`, {
        templateType: type,
        subject: t.subject?.trim() ?? '',
        htmlBody: t.htmlBody?.trim() ?? '',
        ctaButtonText: t.ctaButtonText?.trim() || null,
        ctaUrlOverride: t.ctaUrlOverride?.trim() || null,
        isDefault: t.isDefault ?? false,
      });
      notify.success('Email template saved', { key: 'etpl-save' });
    } catch (e) {
      notify.fail(e, 'etpl-save');
    } finally {
      busy = null;
    }
  }

  async function reset() {
    if (
      !(await confirm({
        title: 'Reset to the default template?',
        message: 'The subject, body and button text return to the built-in version.',
        confirm: 'Reset template',
        danger: true,
      }))
    )
      return;
    busy = 'reset';
    try {
      await api.post(`/api/admin/notification-templates/${type}/reset`);
      await load(type);
      notify.success('Template reset');
    } catch (e) {
      notify.fail(e);
    } finally {
      busy = null;
    }
  }
</script>

<div class="fields">
  <div class="field narrow">
    <label for="e-type">Notification</label>
    <select id="e-type" bind:value={type}
      ><option value="">Choose</option>{#each TYPES as [value, label]}<option {value}>{label}</option>{/each}</select
    >
  </div>
  {#if t}
    <div class="field">
      <label for="e-subject" use:tooltip={'Supports placeholders in double braces'}>Subject</label><input
        id="e-subject"
        bind:value={t.subject}
      />
    </div>
    <div class="field">
      <label for="e-body">HTML body</label><CodeEditor id="e-body" value={t.htmlBody ?? ''} rows={16} onchange={(v) => (t.htmlBody = v)} />
    </div>
    <div class="grid2">
      <div class="field"><label for="e-cta">Button text</label><input id="e-cta" bind:value={t.ctaButtonText} /></div>
      <div class="field">
        <label for="e-ctaurl" use:tooltip={'Overrides the Reef URL from the notification settings'}>Button URL</label><input
          id="e-ctaurl"
          type="url"
          bind:value={t.ctaUrlOverride}
        />
      </div>
    </div>
    <div class="row">
      <button type="button" class="btn primary" aria-busy={busy === 'save'} onclick={save}>Save template</button>
      <button type="button" class="btn" onclick={() => (showPreview = !showPreview)}>{showPreview ? 'Hide preview' : 'Preview'}</button>
      <button type="button" class="btn danger" aria-busy={busy === 'reset'} onclick={reset}>Reset to default</button>
    </div>
    {#if showPreview}<iframe class="frame" sandbox="" title="Email template preview" srcdoc={t.htmlBody ?? ''}></iframe>{/if}
  {/if}
</div>

<style>
  .narrow {
    max-width: 320px;
  }
  .row {
    display: flex;
    gap: 8px;
  }
  .frame {
    width: 100%;
    height: 480px;
    border: 1px solid var(--line);
    border-radius: var(--radius);
    background: #ffffff;
  }
</style>
