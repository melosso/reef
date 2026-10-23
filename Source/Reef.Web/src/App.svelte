<script lang="ts">
  import type { Component } from 'svelte';
  import { Toaster } from 'svelte-sonner';
  import { match, navigate, route } from './lib/router.svelte';
  import { refresh, restoreFromCookie, session } from './lib/session.svelte';
  import { theme } from './lib/theme.svelte';
  import Sidebar from './components/Sidebar.svelte';
  import CommandBar from './components/CommandBar.svelte';
  import ConfirmDialog from './components/ConfirmDialog.svelte';
  import PasswordRequired from './components/PasswordRequired.svelte';

  type Loader = () => Promise<{ default: Component }>;
  interface Route {
    pattern: string;
    load: Loader;
    public?: boolean;
  }

  const routes: Route[] = [
    { pattern: '/', load: () => import('./pages/Login.svelte'), public: true },
    { pattern: '/index', load: () => import('./pages/Login.svelte'), public: true },
    { pattern: '/logoff', load: () => import('./pages/Logoff.svelte'), public: true },
    { pattern: '/dashboard', load: () => import('./pages/Dashboard.svelte') },
    { pattern: '/profiles', load: () => import('./pages/Profiles.svelte') },
    { pattern: '/profiles/:kind/new', load: () => import('./pages/ProfileEditor.svelte') },
    { pattern: '/profiles/:kind/:id', load: () => import('./pages/ProfileEditor.svelte') },
    { pattern: '/connections', load: () => import('./pages/Connections.svelte') },
    { pattern: '/destinations', load: () => import('./pages/Destinations.svelte') },
    { pattern: '/templates', load: () => import('./pages/Templates.svelte') },
    { pattern: '/jobs', load: () => import('./pages/Jobs.svelte') },
    { pattern: '/executions', load: () => import('./pages/Executions.svelte') },
    { pattern: '/email-approvals', load: () => import('./pages/Approvals.svelte') },
    { pattern: '/groups', load: () => import('./pages/Groups.svelte') },
    { pattern: '/admin', load: () => import('./pages/Admin.svelte') },
    { pattern: '/account', load: () => import('./pages/Account.svelte') },
  ];

  const resolved = $derived.by(() => {
    for (const r of routes) {
      const params = match(r.pattern, route.path);
      if (params) return { route: r, params };
    }
    return { route: { pattern: '*', load: () => import('./pages/NotFound.svelte') } as Route, params: {} };
  });

  let restoring = $state(false);

  $effect(() => {
    if (resolved.route.public || session.token || restoring) return;
    restoring = true;
    restoreFromCookie().then((ok) => {
      restoring = false;
      if (!ok) navigate(`/index?next=${encodeURIComponent(route.path)}`, true);
    });
  });

  $effect(() => {
    if (!session.token) return;
    const timer = setInterval(refresh, 20 * 60 * 1000);
    return () => clearInterval(timer);
  });

  const toasterTheme = $derived(theme.choice);
</script>

{#if resolved.route.public}
  {#await resolved.route.load() then page}
    <page.default />
  {/await}
{:else if session.token}
  <div class="shell">
    <Sidebar />
    <main class="main">
      {#key resolved.route.pattern + JSON.stringify(resolved.params)}
        {#await resolved.route.load() then page}
          <page.default />
        {/await}
      {/key}
    </main>
  </div>
  <CommandBar />
  <PasswordRequired />
{/if}

<ConfirmDialog />
<Toaster position="bottom-right" theme={toasterTheme} closeButton={false} visibleToasts={4} toastOptions={{ class: 'reef-toast' }} />

<style>
  .shell {
    display: flex;
    height: 100%;
    min-width: 768px;
  }
  .main {
    flex: 1;
    min-width: 0;
    display: flex;
    flex-direction: column;
  }
  :global(.reef-toast) {
    border-radius: var(--radius) !important;
    font-family: var(--font) !important;
    font-size: 13px !important;
    cursor: copy;
  }
  :global(.reef-toast [data-title]) {
    font-weight: 500 !important;
  }
</style>
