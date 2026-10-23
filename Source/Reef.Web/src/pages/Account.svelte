<script lang="ts">
  import Page from '../components/Page.svelte';
  import StatusMark from '../components/StatusMark.svelte';
  import { api } from '../lib/api';
  import { confirm } from '../lib/confirm.svelte';
  import { notify } from '../lib/notify';
  import { navigate, route, setQuery } from '../lib/router.svelte';
  import { session } from '../lib/session.svelte';

  type Tab = 'profile' | 'password' | 'mfa';
  const tab = $derived((route.query.get('tab') as Tab) || 'profile');

  let profile = $state<any>({ username: session.username, displayName: '', email: '' });
  let current = $state('');
  let next = $state('');
  let busy = $state<string | null>(null);
  let totp = $state<{ qr: string; key: string } | null>(null);
  let reconfirm = $state(false);
  let reconfirmCode = $state('');
  let verifyCode = $state('');

  async function load() {
    profile = await api.get('/api/account/profile').catch((e) => {
      notify.fail(e);
      return profile;
    });
  }

  $effect(() => {
    load();
  });

  const totpActive = $derived(profile.mfaEnabled && profile.mfaMethod === 'totp');
  const emailActive = $derived(profile.mfaEnabled && profile.mfaMethod === 'email');

  async function step(name: string, fn: () => Promise<void>) {
    busy = name;
    try {
      await fn();
    } catch (e) {
      notify.fail(e, `account-${name}`);
    } finally {
      busy = null;
    }
  }

  function ensure(data: any, fallback: string) {
    if (data && data.success === false) throw new Error(data.message || fallback);
  }

  const saveProfile = (e: SubmitEvent) => {
    e.preventDefault();
    return step('profile', async () => {
      ensure(
        await api.put('/api/account/profile', { displayName: profile.displayName?.trim(), email: profile.email?.trim() }),
        'Profile save failed.',
      );
      session.displayName = profile.displayName || null;
      try {
        localStorage.setItem('reef_display_name', profile.displayName ?? '');
      } catch {}
      notify.success('Profile saved', { key: 'account-profile' });
    });
  };

  const changePassword = (e: SubmitEvent) => {
    e.preventDefault();
    return step('password', async () => {
      ensure(await api.post('/api/auth/change-password', { currentPassword: current, newPassword: next }), 'Password change failed.');
      notify.success('Password changed. Sign in with the new password.', { key: 'account-password' });
      setTimeout(() => navigate('/logoff'), 1500);
    });
  };

  function showSetup(data: any) {
    totp = { qr: data.qrCodePng, key: data.secretBase32.match(/.{1,4}/g)?.join(' ') ?? data.secretBase32 };
    verifyCode = '';
  }

  const startTotp = () => step('totp', async () => showSetup(await api.post('/api/account/mfa/totp/setup', {})));

  const submitReconfirm = (e: SubmitEvent) => {
    e.preventDefault();
    return step('reconfirm', async () => {
      showSetup(await api.post('/api/account/mfa/totp/setup', { currentCode: reconfirmCode.replace(/\s/g, '') }));
      reconfirm = false;
      reconfirmCode = '';
    });
  };

  const confirmTotp = (e: SubmitEvent) => {
    e.preventDefault();
    return step('verify', async () => {
      const data = await api.post('/api/account/mfa/totp/confirm', { code: verifyCode.replace(/\s/g, '') });
      ensure(data, 'Invalid code.');
      totp = null;
      profile = { ...profile, mfaEnabled: true, mfaMethod: 'totp' };
      notify.success('Authenticator app is active');
      if (data.backupCodes?.length) downloadBackupCodes(data.backupCodes);
    });
  };

  function downloadBackupCodes(codes: string[]) {
    const lines = [
      '# Reef two-factor authentication backup codes',
      `# Account: ${profile.username}`,
      `# Generated: ${new Date().toISOString().split('T')[0]}`,
      '# Each code works once.',
      '',
      'backup_codes:',
      ...codes.map((c) => `  - ${c}`),
    ];
    const a = document.createElement('a');
    a.href = URL.createObjectURL(new Blob([lines.join('\n')], { type: 'text/yaml' }));
    a.download = `reef-backup-codes-${profile.username}.yaml`;
    a.click();
    URL.revokeObjectURL(a.href);
  }

  const enableEmail = () =>
    step('email', async () => {
      ensure(await api.post('/api/account/mfa/email/enable'), 'Email codes could not be turned on.');
      profile = { ...profile, mfaEnabled: true, mfaMethod: 'email' };
      notify.success('Email codes are active');
    });

  const disableMfa = async () => {
    if (
      !(await confirm({
        title: 'Turn off two-factor authentication?',
        message: 'Sign-in requires only the password.',
        confirm: 'Turn off',
        danger: true,
      }))
    )
      return;
    await step('disable', async () => {
      ensure(await api.del('/api/account/mfa'), 'Two-factor authentication could not be turned off.');
      profile = { ...profile, mfaEnabled: false, mfaMethod: null };
      notify.success('Two-factor authentication is off');
    });
  };

  const emailHint = $derived(
    emailActive
      ? `Codes are sent to ${profile.email || 'the account email address'}.`
      : totpActive
        ? 'Turn off the authenticator app to use email codes.'
        : !profile.emailMfaAvailable
          ? 'Requires email notifications in Admin settings.'
          : !profile.email
            ? 'Set an email address on the Profile tab.'
            : `Codes are sent to ${profile.email}.`,
  );
</script>

<Page crumbs={[{ label: 'Account' }]}>
  <div class="page-body narrow">
    <div class="tabs" role="tablist">
      <button type="button" role="tab" aria-selected={tab === 'profile'} onclick={() => setQuery('tab', null)}>Profile</button>
      <button type="button" role="tab" aria-selected={tab === 'password'} onclick={() => setQuery('tab', 'password')}>Password</button>
      <button type="button" role="tab" aria-selected={tab === 'mfa'} onclick={() => setQuery('tab', 'mfa')}
        >Two-factor authentication</button
      >
    </div>

    {#if tab === 'profile'}
      <form class="panel panel-body fields" onsubmit={saveProfile}>
        <div class="field"><label for="a-user">Username</label><input id="a-user" value={profile.username ?? ''} readonly /></div>
        <div class="field">
          <label for="a-display">Display name</label><input id="a-display" bind:value={profile.displayName} autocomplete="name" />
        </div>
        <div class="field">
          <label for="a-email">Email address</label><input id="a-email" type="email" bind:value={profile.email} autocomplete="email" />
        </div>
        <div><button type="submit" class="btn primary" aria-busy={busy === 'profile'}>Save profile</button></div>
      </form>
    {:else if tab === 'password'}
      <form class="panel panel-body fields" onsubmit={changePassword}>
        <div class="field">
          <label for="a-current">Current password</label><input
            id="a-current"
            type="password"
            autocomplete="current-password"
            bind:value={current}
          />
        </div>
        <div class="field">
          <label for="a-new">New password</label><input id="a-new" type="password" autocomplete="new-password" bind:value={next} />
        </div>
        <p class="muted">Changing the password signs this account out.</p>
        <div><button type="submit" class="btn primary" aria-busy={busy === 'password'}>Change password</button></div>
      </form>
    {:else}
      <section class="panel">
        <div class="panel-head">
          <h2>Authenticator app</h2>
          <span class="grow"></span>
          <StatusMark kind={totpActive ? 'ok' : 'off'} label={totpActive ? 'Active' : 'Off'} />
        </div>
        <div class="panel-body fields">
          <p class="muted">Time-based codes from an app such as Aegis, 1Password or Google Authenticator.</p>
          {#if totp}
            <div class="setup">
              <img src="data:image/png;base64,{totp.qr}" alt="QR code for the authenticator app" width="160" height="160" />
              <div class="fields">
                <div>
                  <span class="muted">Manual key</span>
                  <div class="num key">{totp.key}</div>
                </div>
                <form class="fields" onsubmit={confirmTotp}>
                  <div class="field">
                    <label for="a-verify">Code from the app</label><input
                      id="a-verify"
                      class="number"
                      inputmode="numeric"
                      autocomplete="one-time-code"
                      maxlength="7"
                      bind:value={verifyCode}
                    />
                  </div>
                  <div class="row">
                    <button type="submit" class="btn primary" aria-busy={busy === 'verify'}>Activate</button><button
                      type="button"
                      class="btn"
                      onclick={() => (totp = null)}>Cancel</button
                    >
                  </div>
                </form>
              </div>
            </div>
          {:else if reconfirm}
            <form class="fields" onsubmit={submitReconfirm}>
              <div class="field">
                <label for="a-reconfirm">Code from the current app</label><input
                  id="a-reconfirm"
                  class="number"
                  inputmode="numeric"
                  autocomplete="one-time-code"
                  maxlength="7"
                  bind:value={reconfirmCode}
                />
              </div>
              <div class="row">
                <button type="submit" class="btn primary" aria-busy={busy === 'reconfirm'}>Continue</button><button
                  type="button"
                  class="btn"
                  onclick={() => (reconfirm = false)}>Cancel</button
                >
              </div>
            </form>
          {:else}
            <div class="row">
              <button
                type="button"
                class="btn"
                disabled={emailActive}
                aria-busy={busy === 'totp'}
                onclick={() => (totpActive ? (reconfirm = true) : startTotp())}>{totpActive ? 'Reconfigure' : 'Set up'}</button
              >
            </div>
          {/if}
        </div>
      </section>
      <section class="panel">
        <div class="panel-head">
          <h2>Email codes</h2>
          <span class="grow"></span>
          <StatusMark kind={emailActive ? 'ok' : 'off'} label={emailActive ? 'Active' : 'Off'} />
        </div>
        <div class="panel-body fields">
          <p class="muted">{emailHint}</p>
          <div class="row">
            <button
              type="button"
              class="btn"
              aria-busy={busy === 'email'}
              disabled={totpActive || (!emailActive && (!profile.emailMfaAvailable || !profile.email))}
              onclick={enableEmail}>{emailActive ? 'Reconfigure' : 'Turn on'}</button
            >
          </div>
        </div>
      </section>
      {#if profile.mfaEnabled}
        <div>
          <button type="button" class="btn danger" aria-busy={busy === 'disable'} onclick={disableMfa}
            >Turn off two-factor authentication</button
          >
        </div>
      {/if}
    {/if}
  </div>
</Page>

<style>
  .narrow > * {
    max-width: 640px;
    width: 100%;
  }
  .grow {
    flex: 1;
  }
  .row {
    display: flex;
    gap: 8px;
  }
  .setup {
    display: flex;
    gap: 20px;
    align-items: flex-start;
  }
  .setup img {
    border: 1px solid var(--line);
    border-radius: var(--radius);
    background: #ffffff;
  }
  .key {
    font-size: 14px;
    margin-top: 4px;
    word-break: break-all;
  }
</style>
