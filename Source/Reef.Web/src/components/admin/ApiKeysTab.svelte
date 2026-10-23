<script lang="ts">
  import { Copy, Plus, Trash2 } from '@lucide/svelte';
  import StatusMark from '../StatusMark.svelte';
  import { api } from '../../lib/api';
  import type { ApiKey } from '../../models/ApiKey';
  import type { ApiKeyCreated } from '../../models/ApiKeyCreated';
  import { confirm } from '../../lib/confirm.svelte';
  import { formatDateTime, parseDate } from '../../lib/format';
  import { copyText, notify } from '../../lib/notify';
  import { tooltip } from '../../lib/tooltip';

  let keys = $state<ApiKey[]>([]);
  let dialog: HTMLDialogElement;
  let name = $state('');
  let permissions = $state('{"read":true,"write":false}');
  let expiresAt = $state('');
  let created = $state('');
  let busy = $state(false);

  async function load() {
    keys = await api.get<ApiKey[]>('/api/admin/api-keys').catch((e) => {
      notify.fail(e, 'apikeys');
      return [];
    });
  }

  $effect(() => {
    load();
  });

  function open() {
    name = '';
    permissions = '{"read":true,"write":false}';
    expiresAt = '';
    created = '';
    dialog.showModal();
  }

  async function create(e: SubmitEvent) {
    e.preventDefault();
    busy = true;
    try {
      const r = await api.post<ApiKeyCreated>('/api/admin/api-keys', { name: name.trim(), permissions, expiresAt: expiresAt || null });
      created = r.apiKey;
      await load();
    } catch (err) {
      notify.fail(err, 'apikey-create');
    } finally {
      busy = false;
    }
  }

  async function remove(k: ApiKey) {
    if (
      !(await confirm({
        title: `Revoke ${k.name}?`,
        message: 'Requests with this key fail after revocation.',
        confirm: 'Revoke key',
        danger: true,
      }))
    )
      return;
    try {
      await api.del(`/api/admin/api-keys/${k.id}`);
      notify.success(`${k.name} revoked`);
      await load();
    } catch (e) {
      notify.fail(e);
    }
  }

  const expired = (k: ApiKey) => !!k.expiresAt && (parseDate(k.expiresAt)?.getTime() ?? Infinity) < Date.now();
</script>

<div class="toolbar">
  <p class="muted">API keys authenticate scripts and integrations with the <span class="num">X-Api-Key</span> header.</p>
  <span class="spacer"></span>
  <button type="button" class="btn primary" onclick={open}><Plus size={15} />New API key</button>
</div>
<div class="table-wrap">
  <table class="data">
    <thead
      ><tr><th>Name</th><th>Status</th><th>Created</th><th>Expires</th><th>Last used</th><th><span class="sr">Actions</span></th></tr
      ></thead
    >
    <tbody>
      {#each keys as k (k.id)}
        <tr>
          <td class="strong">{k.name}</td>
          <td
            ><StatusMark
              kind={expired(k) || k.isActive === false ? 'off' : 'ok'}
              label={expired(k) ? 'Expired' : k.isActive === false ? 'Off' : 'Active'}
            /></td
          >
          <td>{formatDateTime(k.createdAt)}</td>
          <td>{k.expiresAt ? formatDateTime(k.expiresAt) : 'Never'}</td>
          <td>{k.lastUsedAt ? formatDateTime(k.lastUsedAt) : 'Never'}</td>
          <td class="actions"
            ><button
              type="button"
              class="btn ghost icon danger"
              aria-label="Revoke {k.name}"
              use:tooltip={'Revoke'}
              onclick={() => remove(k)}><Trash2 size={15} /></button
            ></td
          >
        </tr>
      {:else}
        <tr><td colspan="6" class="empty">No API keys</td></tr>
      {/each}
    </tbody>
  </table>
</div>

<dialog bind:this={dialog} aria-labelledby="key-title">
  {#if created}
    <div class="dbody fields">
      <h2 id="key-title">API key created</h2>
      <p class="muted">The key is displayed once. Store it in a password manager.</p>
      <div class="key">
        <span class="num">{created}</span><button
          type="button"
          class="btn ghost icon"
          aria-label="Copy API key"
          onclick={() => copyText(created)}><Copy size={14} /></button
        >
      </div>
    </div>
    <div class="dfoot"><button type="button" class="btn primary" onclick={() => dialog.close()}>Done</button></div>
  {:else}
    <form onsubmit={create}>
      <div class="dbody fields">
        <h2 id="key-title">New API key</h2>
        <div class="field"><label for="k-name">Name</label><input id="k-name" bind:value={name} autocomplete="off" /></div>
        <div class="field">
          <label for="k-perm">Permissions in JSON</label><textarea id="k-perm" rows="2" class="number" bind:value={permissions}></textarea>
        </div>
        <div class="field">
          <label for="k-exp" use:tooltip={'Leave empty for a key without expiry'}>Expires</label><input
            id="k-exp"
            type="datetime-local"
            bind:value={expiresAt}
          />
        </div>
      </div>
      <div class="dfoot">
        <button type="button" class="btn" onclick={() => dialog.close()}>Cancel</button>
        <button type="submit" class="btn primary" aria-busy={busy}>Create key</button>
      </div>
    </form>
  {/if}
</dialog>

<style>
  .spacer {
    flex: 1;
  }
  .strong {
    font-weight: 500;
  }
  .dbody {
    padding: 20px;
    min-width: 420px;
  }
  .dfoot {
    display: flex;
    justify-content: flex-end;
    gap: 8px;
    padding: 12px 20px;
    border-top: 1px solid var(--line);
  }
  .key {
    display: flex;
    align-items: center;
    gap: 8px;
    padding: 10px 12px;
    background: var(--bg);
    border: 1px solid var(--line);
    border-radius: var(--radius);
    word-break: break-all;
  }
</style>
