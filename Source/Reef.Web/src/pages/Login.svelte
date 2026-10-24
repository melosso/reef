<script lang="ts">
  import type { LoginResult } from '../models/LoginResult';
  import { onMount } from 'svelte';
  import Logo from '../components/Logo.svelte';
  import { request } from '../lib/api';
  import { navigate, route } from '../lib/router.svelte';
  import { session, storeSession } from '../lib/session.svelte';
  import { notify } from '../lib/notify';
  import { tooltip } from '../lib/tooltip';

  const OIDC_PROBLEMS: Record<string, string> = {
    failed: 'OIDC sign-in failed. Try again.',
    denied: 'OIDC sign-in was cancelled.',
    no_account: 'No Reef account matches this OIDC identity.',
    disabled: 'OIDC sign-in is turned off.',
  };

  let username = $state('');
  let step = $state<'email' | 'password'>('email');
  let password = $state('');
  let code = $state('');
  let busy = $state(false);
  let mfa = $state<{ sessionId: string; method: string } | null>(null);
  let oidc = $state<{ enabled: boolean; name: string } | null>(null);

  const next = () => {
    const target = route.query.get('next');
    const page = target?.split('?')[0].toLowerCase();
    return target && target.startsWith('/') && !target.startsWith('//') && !['/', '/index', '/login', '/logout', '/logoff'].includes(page!)
      ? target
      : '/dashboard';
  };

  onMount(() => {
    const problem = route.query.get('sso');
    if (problem) notify.error(OIDC_PROBLEMS[problem] ?? OIDC_PROBLEMS.failed);
    if (session.token) navigate(next(), true);
    fetch('/api/auth/oidc/status')
      .then((r) => r.json())
      .then((d) => (oidc = d))
      .catch(() => {});
  });

  function proceed(e: SubmitEvent) {
    e.preventDefault();
    step = 'password';
  }

  async function signIn(e: SubmitEvent) {
    e.preventDefault();
    busy = true;
    try {
      const data = await request<LoginResult>('POST', '/api/auth/login', { username, password });
      if (data?.mfaRequired) {
        mfa = { sessionId: data.mfaSessionId ?? '', method: data.mfaMethod ?? '' };
        return;
      }
      storeSession(data);
      navigate(next(), true);
    } catch (error) {
      notify.fail(error, 'sign-in');
    } finally {
      busy = false;
    }
  }

  async function verify(e?: SubmitEvent) {
    e?.preventDefault();
    if (!mfa || code.length !== 6) return;
    busy = true;
    try {
      const data = await request<LoginResult>('POST', '/api/auth/mfa', { mfaSessionId: mfa.sessionId, code });
      storeSession(data);
      navigate(next(), true);
    } catch (error) {
      code = '';
      notify.fail(error, 'sign-in');
    } finally {
      busy = false;
    }
  }

  async function resend() {
    if (!mfa) return;
    try {
      await request('POST', '/api/auth/resend-otp', { mfaSessionId: mfa.sessionId });
      notify.success('New code sent to your email address.');
    } catch (error) {
      notify.fail(error);
    }
  }

  $effect(() => {
    if (code.length === 6) verify();
  });

  function focus(node: HTMLInputElement) {
    node.focus();
  }
</script>

<svelte:head><title>Sign in · Reef</title></svelte:head>

<div class="wrap">
  <div class="card">
    <div class="brand"><Logo size={26} /><span>Reef</span></div>
    {#if !mfa}
      <h1>Sign in</h1>
      {#if step === 'email'}
        <form onsubmit={proceed} class="fields">
          <div class="field">
            <label for="username" use:tooltip={'Email address or username'}>Email</label>
            <input
              id="username"
              bind:value={username}
              inputmode="email"
              autocomplete="username"
              autocapitalize="none"
              spellcheck="false"
              use:focus
            />
          </div>
          <button class="btn primary full" type="submit">Continue</button>
        </form>
      {:else}
        <form onsubmit={signIn} class="fields">
          <div class="who">
            <span>{username}</span>
            <button
              type="button"
              class="link"
              onclick={() => {
                step = 'email';
                password = '';
              }}>Use another email</button
            >
          </div>
          <input class="sr" type="text" autocomplete="username" value={username} readonly tabindex="-1" aria-hidden="true" />
          <div class="field">
            <label for="password">Password</label>
            <input id="password" type="password" bind:value={password} autocomplete="current-password" use:focus />
          </div>
          <button class="btn primary full" type="submit" aria-busy={busy} disabled={busy}>Sign in</button>
        </form>
      {/if}
      {#if oidc?.enabled}
        <div class="or"><span>or</span></div>
        <a class="btn full" href="/api/auth/oidc/start" rel="nofollow">Sign in with {oidc.name}</a>
      {/if}
    {:else}
      <h1>Verification code</h1>
      <p class="muted">
        {mfa.method === 'email'
          ? 'Enter the 6-digit code sent to your email address.'
          : 'Enter the 6-digit code from your authenticator app.'}
      </p>
      <form onsubmit={verify} class="fields">
        <div class="field">
          <label for="code" class="sr">Verification code</label>
          <input id="code" class="code" bind:value={code} inputmode="numeric" autocomplete="one-time-code" maxlength="6" use:focus />
        </div>
        <button class="btn primary full" type="submit" aria-busy={busy} disabled={busy || code.length !== 6}>Verify</button>
      </form>
      <div class="links">
        {#if mfa.method === 'email'}<button type="button" class="link" onclick={resend}>Send a new code</button>{/if}
        <button
          type="button"
          class="link"
          onclick={() => {
            mfa = null;
            code = '';
          }}>Use another account</button
        >
      </div>
    {/if}
  </div>
  <footer class="muted">Licensed under the EUPL 1.2</footer>
</div>

<style>
  .wrap {
    min-height: 100%;
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    gap: 24px;
    padding: 24px;
  }
  .card {
    width: 360px;
    max-width: 100%;
    padding: 28px;
    background: var(--surface);
    border: 1px solid var(--line);
    border-radius: var(--radius);
    display: flex;
    flex-direction: column;
    gap: 18px;
  }
  .brand {
    display: flex;
    align-items: center;
    gap: 10px;
    font-size: 18px;
    font-weight: 600;
  }
  h1 {
    font-size: 20px;
  }
  .full {
    width: 100%;
    justify-content: center;
    padding: 9px 12px;
  }
  .or {
    display: flex;
    align-items: center;
    gap: 12px;
    color: var(--muted);
    font-size: 12px;
  }
  .or::before,
  .or::after {
    content: '';
    flex: 1;
    border-top: 1px solid var(--line);
  }
  .code {
    font-family: var(--mono);
    font-size: 22px !important;
    letter-spacing: 0.4em;
    text-align: center;
  }
  .who {
    display: flex;
    justify-content: space-between;
    align-items: center;
    gap: 12px;
    padding: 8px 10px;
    background: var(--bg);
    border: 1px solid var(--line);
    border-radius: var(--radius);
    overflow-wrap: anywhere;
  }
  .links {
    display: flex;
    justify-content: space-between;
    font-size: 13px;
  }
  footer {
    font-size: 12px;
  }
  footer a {
    color: var(--muted);
  }
</style>
