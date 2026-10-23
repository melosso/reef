<script lang="ts">
  import { Pencil, Plus, Trash2 } from '@lucide/svelte';
  import Sheet from '../Sheet.svelte';
  import StatusMark from '../StatusMark.svelte';
  import { api } from '../../lib/api';
  import { confirm } from '../../lib/confirm.svelte';
  import { formatDateTime, formatRelative } from '../../lib/format';
  import { notify } from '../../lib/notify';
  import { session } from '../../lib/session.svelte';
  import { tooltip } from '../../lib/tooltip';

  interface Form {
    id?: number;
    username: string;
    newUsername: string;
    displayName: string;
    password: string;
    role: string;
    isActive: boolean;
  }

  let users = $state<any[]>([]);
  let form = $state<Form | null>(null);
  let busy = $state(false);

  async function load() {
    users = await api.get<any[]>('/api/admin/users').catch((e) => {
      notify.fail(e, 'users');
      return [];
    });
  }

  $effect(() => {
    load();
  });

  function edit(u: any) {
    form = {
      id: u.id,
      username: u.username,
      newUsername: u.username,
      displayName: u.displayName ?? '',
      password: '',
      role: u.role,
      isActive: u.isActive,
    };
  }

  async function save(e: SubmitEvent) {
    e.preventDefault();
    if (!form) return;
    busy = true;
    try {
      if (!form.id) {
        await api.post('/api/admin/users', { username: form.username.trim(), password: form.password, role: form.role });
      } else {
        if (form.newUsername.trim() && form.newUsername.trim() !== form.username) {
          await api.put(`/api/admin/users/${form.id}/username`, { newUsername: form.newUsername.trim() });
        }
        await api.put(`/api/admin/users/${form.id}`, {
          role: form.role,
          isActive: form.isActive,
          displayName: form.displayName.trim() || null,
          ...(form.password ? { password: form.password } : {}),
        });
      }
      notify.success(`${form.newUsername || form.username} saved`, { key: 'user-save' });
      form = null;
      await load();
    } catch (err) {
      notify.fail(err, 'user-save');
    } finally {
      busy = false;
    }
  }

  async function remove(u: any) {
    if (
      !(await confirm({
        title: `Delete ${u.username}?`,
        message: 'The account loses access. Audit entries keep the username.',
        confirm: 'Delete user',
        danger: true,
      }))
    )
      return;
    try {
      await api.del(`/api/admin/users/${u.id}`);
      notify.success(`${u.username} deleted`);
      await load();
    } catch (e) {
      notify.fail(e);
    }
  }
</script>

<div class="toolbar">
  <span class="spacer"></span><button
    type="button"
    class="btn primary"
    onclick={() => (form = { username: '', newUsername: '', displayName: '', password: '', role: 'User', isActive: true })}
    ><Plus size={15} />New user</button
  >
</div>
<div class="table-wrap">
  <table class="data">
    <thead
      ><tr><th>Username</th><th>Role</th><th>Status</th><th>Last seen</th><th>Created</th><th><span class="sr">Actions</span></th></tr
      ></thead
    >
    <tbody>
      {#each users as u (u.id)}
        {@const seen = u.lastLoginAt || u.lastSeenAt || u.LastSeenAt}
        <tr>
          <td
            ><span class="strong">{u.username}</span>{#if u.displayName}<span class="muted">
                {u.displayName}</span
              >{/if}{#if u.username === session.username}<span class="muted"> (you)</span>{/if}</td
          >
          <td>{u.role}</td>
          <td><StatusMark kind={u.isActive ? 'ok' : 'off'} label={u.isActive ? 'Active' : 'Off'} /></td>
          <td><span use:tooltip={formatDateTime(seen)}>{seen ? formatRelative(seen) : 'Never'}</span></td>
          <td>{formatDateTime(u.createdAt)}</td>
          <td class="actions">
            <button type="button" class="btn ghost icon" aria-label="Edit {u.username}" use:tooltip={'Edit'} onclick={() => edit(u)}
              ><Pencil size={15} /></button
            >
            {#if u.id !== 1 && u.username !== session.username}<button
                type="button"
                class="btn ghost icon danger"
                aria-label="Delete {u.username}"
                use:tooltip={'Delete'}
                onclick={() => remove(u)}><Trash2 size={15} /></button
              >{/if}
          </td>
        </tr>
      {:else}
        <tr><td colspan="6" class="empty">No users</td></tr>
      {/each}
    </tbody>
  </table>
</div>

{#if form}
  <Sheet title={form.id ? form.username : 'New user'} onclose={() => (form = null)}>
    <form id="user-form" class="fields" onsubmit={save}>
      {#if form.id}
        <div class="field">
          <label for="u-name">Username</label><input id="u-name" bind:value={form.newUsername} autocomplete="off" autocapitalize="none" />
        </div>
        <div class="field"><label for="u-display">Display name</label><input id="u-display" bind:value={form.displayName} /></div>
      {:else}
        <div class="field">
          <label for="u-name">Username</label><input id="u-name" bind:value={form.username} autocomplete="off" autocapitalize="none" />
        </div>
      {/if}
      <div class="field">
        <label
          for="u-role"
          use:tooltip={form.id === 1 ? 'The first administrator keeps the Admin role' : 'Admins manage users, API keys and settings'}
          >Role</label
        >
        <select id="u-role" bind:value={form.role} disabled={form.id === 1}
          ><option value="User">User</option><option value="Admin">Admin</option></select
        >
      </div>
      <div class="field">
        <label for="u-pass" use:tooltip={form.id ? 'Leave empty to keep the current password' : null}
          >{form.id ? 'New password' : 'Password'}</label
        ><input id="u-pass" type="password" autocomplete="new-password" bind:value={form.password} />
      </div>
      {#if form.id}<label class="check"><input type="checkbox" bind:checked={form.isActive} />Active</label>{/if}
    </form>
    {#snippet footer()}
      <button type="button" class="btn" onclick={() => (form = null)}>Cancel</button>
      <button type="submit" form="user-form" class="btn primary" aria-busy={busy}>Save</button>
    {/snippet}
  </Sheet>
{/if}

<style>
  .spacer {
    flex: 1;
  }
  .strong {
    font-weight: 500;
  }
</style>
