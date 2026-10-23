<script lang="ts">
  import { onMount } from 'svelte';
  import { api } from '../lib/api';
  import { notify } from '../lib/notify';
  import { navigate } from '../lib/router.svelte';
  import { session } from '../lib/session.svelte';

  let dialog: HTMLDialogElement;
  let current = $state('');
  let next = $state('');
  let busy = $state(false);

  onMount(async () => {
    const data = await api.post('/api/auth/validate', { token: session.token }).catch(() => null);
    if (data?.valid && data.passwordChangeRequired) dialog.showModal();
  });

  async function submit(e: SubmitEvent) {
    e.preventDefault();
    busy = true;
    try {
      const data = await api.post('/api/auth/change-password', { currentPassword: current, newPassword: next });
      notify.success('Password changed');
      dialog.close();
      if (data?.requiresLogoff) navigate('/logoff');
    } catch (error) {
      notify.fail(error, 'password-required');
    } finally {
      busy = false;
    }
  }
</script>

<dialog bind:this={dialog} oncancel={(e) => e.preventDefault()} aria-labelledby="pw-title">
  <form onsubmit={submit}>
    <div class="body fields">
      <h2 id="pw-title">Change your password</h2>
      <p class="muted">An administrator requires a new password for this account.</p>
      <div class="field">
        <label for="pw-current">Current password</label><input
          id="pw-current"
          type="password"
          autocomplete="current-password"
          bind:value={current}
        />
      </div>
      <div class="field">
        <label for="pw-new">New password</label><input id="pw-new" type="password" autocomplete="new-password" bind:value={next} />
      </div>
    </div>
    <div class="foot"><button type="submit" class="btn primary" aria-busy={busy} disabled={busy}>Change password</button></div>
  </form>
</dialog>

<style>
  .body {
    padding: 20px;
    min-width: 380px;
  }
  .foot {
    display: flex;
    justify-content: flex-end;
    padding: 12px 20px;
    border-top: 1px solid var(--line);
  }
</style>
