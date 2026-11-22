<script lang="ts">
  import type { Component } from 'svelte';
  import { Toaster } from 'svelte-sonner';
  import { match, navigate, route } from './lib/router.svelte';
  import { canonicalUrl } from './lib/routes';
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
    key?: string;
  }

  const login = () => import('./pages/Login.svelte');
  const connections = () => import('./pages/Connections.svelte');
  const destinations = () => import('./pages/Destinations.svelte');
  const templates = () => import('./pages/Templates.svelte');
  const jobs = () => import('./pages/Jobs.svelte');
  const executions = () => import('./pages/Executions.svelte');
  const approvals = () => import('./pages/Approvals.svelte');
  const admin = () => import('./pages/Admin.svelte');
  const account = () => import('./pages/Account.svelte');

  // Routes with the same key keep the page mounted. Detail routes open a sheet over their list.
  const routes: Route[] = [
    { pattern: '/login', load: login, public: true },
    { pattern: '/logout', load: () => import('./pages/Logout.svelte'), public: true },
    { pattern: '/', load: login, public: true },
    { pattern: '/index', load: login, public: true },
    { pattern: '/logoff', load: () => import('./pages/Logout.svelte'), public: true },
    { pattern: '/dashboard', load: () => import('./pages/Dashboard.svelte') },
    { pattern: '/workflows', load: () => import('./pages/Workflows.svelte') },
    { pattern: '/workflows/:id', load: () => import('./pages/WorkflowEditor.svelte') },
    { pattern: '/connections', load: connections, key: 'connections' },
    { pattern: '/connections/:id', load: connections, key: 'connections' },
    { pattern: '/destinations', load: destinations, key: 'destinations' },
    { pattern: '/destinations/:id', load: destinations, key: 'destinations' },
    { pattern: '/templates', load: templates, key: 'templates' },
    { pattern: '/templates/:id', load: templates, key: 'templates' },
    { pattern: '/jobs', load: jobs, key: 'jobs' },
    { pattern: '/jobs/:id', load: jobs, key: 'jobs' },
    { pattern: '/executions', load: executions, key: 'executions' },
    { pattern: '/executions/:id', load: executions, key: 'executions' },
    { pattern: '/approvals', load: approvals, key: 'approvals' },
    { pattern: '/approvals/:tab', load: approvals, key: 'approvals' },
    { pattern: '/admin', load: admin, key: 'admin' },
    { pattern: '/admin/:tab', load: admin, key: 'admin' },
    { pattern: '/account', load: account, key: 'account' },
    { pattern: '/account/:tab', load: account, key: 'account' },
  ];

  $effect(() => {
    const search = route.query.size ? `?${route.query}` : '';
    const canonical = canonicalUrl(route.path, search);
    if (canonical) navigate(canonical, true);
  });

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
      if (!ok) navigate(`/login?next=${encodeURIComponent(route.path)}`, true);
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
      {#key resolved.route.key ?? resolved.route.pattern + JSON.stringify(resolved.params)}
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
<Toaster position="bottom-right" theme={toasterTheme} closeButton visibleToasts={4} toastOptions={{ class: 'reef-toast' }} />

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
  :global(.reef-toast[data-type='success'] [data-icon]) {
    color: var(--ok);
  }
  :global(.reef-toast[data-type='warning'] [data-icon]) {
    color: var(--warn);
  }
  :global(.reef-toast[data-type='error'] [data-icon]) {
    color: var(--err);
  }
</style>
