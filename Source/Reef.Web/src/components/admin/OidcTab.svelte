<script lang="ts">
  import { Copy } from '@lucide/svelte';
  import { api } from '../../lib/api';
  import { copyText, notify } from '../../lib/notify';
  import { tooltip } from '../../lib/tooltip';

  const redirectUri = `${location.origin}/api/auth/oidc/callback`;
  let form = $state<any>(null);
  let hasSecret = $state(false);
  let busy = $state(false);

  async function load() {
    const d = await api.get('/api/admin/oidc-settings').catch((e) => {
      notify.fail(e);
      return {};
    });
    hasSecret = !!d?.hasClientSecret;
    form = {
      isEnabled: false,
      name: 'OIDC',
      authority: '',
      clientId: '',
      scopes: 'openid profile email',
      usernameClaim: 'preferred_username',
      emailClaim: 'email',
      createAccounts: false,
      ...(d ?? {}),
      clientSecret: '',
    };
  }

  $effect(() => {
    load();
  });

  async function save(e: SubmitEvent) {
    e.preventDefault();
    busy = true;
    try {
      const body = { ...form };
      if (!body.clientSecret) delete body.clientSecret;
      await api.put('/api/admin/oidc-settings', body);
      notify.success('OIDC settings saved', { key: 'oidc-save' });
      await load();
    } catch (err) {
      notify.fail(err, 'oidc-save');
    } finally {
      busy = false;
    }
  }
</script>

{#if form}
  <form class="fields narrow" onsubmit={save}>
    <p class="muted">
      Reef supports one OpenID Connect provider, for example Pocket ID or Microsoft Entra ID. Sign-in matches accounts by username or
      verified email address.
    </p>
    <label class="check"><input type="checkbox" bind:checked={form.isEnabled} />OIDC sign-in enabled</label>
    <div class="field">
      <label for="o-name" use:tooltip={'Displayed on the sign-in button'}>Provider name</label><input
        id="o-name"
        placeholder="Pocket ID"
        bind:value={form.name}
      />
    </div>
    <div class="field">
      <label for="o-auth">Issuer URL</label><input
        id="o-auth"
        type="url"
        placeholder="https://idp.example.com"
        bind:value={form.authority}
      />
    </div>
    <div class="field">
      <label for="o-redirect" use:tooltip={'Register this URL at the provider'}>Redirect URL</label>
      <div class="inline">
        <input id="o-redirect" readonly value={redirectUri} /><button
          type="button"
          class="btn ghost icon"
          aria-label="Copy redirect URL"
          onclick={() => copyText(redirectUri)}><Copy size={14} /></button
        >
      </div>
    </div>
    <div class="grid2">
      <div class="field"><label for="o-client">Client ID</label><input id="o-client" bind:value={form.clientId} autocomplete="off" /></div>
      <div class="field">
        <label for="o-secret">Client secret</label><input
          id="o-secret"
          type="password"
          autocomplete="new-password"
          placeholder={hasSecret ? 'Stored. Type to replace it.' : 'Empty for a public client'}
          bind:value={form.clientSecret}
        />
      </div>
    </div>
    <div class="field"><label for="o-scopes">Scopes</label><input id="o-scopes" bind:value={form.scopes} /></div>
    <div class="grid2">
      <div class="field"><label for="o-user">Username claim</label><input id="o-user" bind:value={form.usernameClaim} /></div>
      <div class="field"><label for="o-email">Email claim</label><input id="o-email" bind:value={form.emailClaim} /></div>
    </div>
    <label class="check" use:tooltip={'Create a User account for OIDC identities without a matching Reef account'}
      ><input type="checkbox" bind:checked={form.createAccounts} />Create accounts on first sign-in</label
    >
    <div><button type="submit" class="btn primary" aria-busy={busy}>Save settings</button></div>
  </form>
{/if}

<style>
  .narrow {
    max-width: 640px;
  }
  .inline {
    display: flex;
    gap: 6px;
  }
</style>
