<script lang="ts">
  import Page from '../components/Page.svelte';
  import ApiKeysTab from '../components/admin/ApiKeysTab.svelte';
  import AuditTab from '../components/admin/AuditTab.svelte';
  import DangerTab from '../components/admin/DangerTab.svelte';
  import EmailTemplatesTab from '../components/admin/EmailTemplatesTab.svelte';
  import InterpretersTab from '../components/admin/InterpretersTab.svelte';
  import LogsTab from '../components/admin/LogsTab.svelte';
  import MetricsTab from '../components/admin/MetricsTab.svelte';
  import NotificationsTab from '../components/admin/NotificationsTab.svelte';
  import OidcTab from '../components/admin/OidcTab.svelte';
  import SystemTab from '../components/admin/SystemTab.svelte';
  import UsersTab from '../components/admin/UsersTab.svelte';
  import { route, setQuery } from '../lib/router.svelte';

  const GROUPS: { title: string; tabs: [string, string, any][] }[] = [
    {
      title: 'Monitoring',
      tabs: [
        ['system', 'System', SystemTab],
        ['metrics', 'Metrics', MetricsTab],
        ['logs', 'Logs', LogsTab],
        ['audit', 'Audit log', AuditTab],
      ],
    },
    {
      title: 'Access',
      tabs: [
        ['users', 'Users', UsersTab],
        ['apikeys', 'API keys', ApiKeysTab],
        ['oidc', 'OIDC', OidcTab],
      ],
    },
    {
      title: 'Configuration',
      tabs: [
        ['notifications', 'Notifications', NotificationsTab],
        ['email-templates', 'Email templates', EmailTemplatesTab],
        ['interpreters', 'Interpreters', InterpretersTab],
      ],
    },
    { title: 'Maintenance', tabs: [['danger', 'Danger zone', DangerTab]] },
  ];
  const ALL = GROUPS.flatMap((g) => g.tabs);

  const tab = $derived(ALL.find(([id]) => id === route.query.get('tab')) ?? ALL[0]);
</script>

<Page crumbs={[{ label: 'Admin', href: '/admin' }, { label: tab[1] }]}>
  <div class="admin">
    <nav class="tablist" aria-label="Admin sections">
      {#each GROUPS as group}
        <h2>{group.title}</h2>
        {#each group.tabs as [id, label]}
          <button
            type="button"
            aria-current={tab[0] === id ? 'page' : undefined}
            onclick={() => setQuery('tab', id === 'system' ? null : id)}>{label}</button
          >
        {/each}
      {/each}
    </nav>
    <div class="page-body">
      {#key tab[0]}
        {@const Tab = tab[2]}
        <Tab />
      {/key}
    </div>
  </div>
</Page>

<style>
  .admin {
    flex: 1;
    min-height: 0;
    display: flex;
  }
  .tablist {
    width: 200px;
    flex: none;
    padding: 16px 12px;
    border-right: 1px solid var(--line);
    background: var(--surface);
    display: flex;
    flex-direction: column;
    gap: 2px;
    overflow: auto;
  }
  .tablist h2 {
    font-size: 12px;
    font-weight: 500;
    color: var(--muted);
    padding: 14px 10px 6px;
  }
  .tablist h2:first-child {
    padding-top: 0;
  }
  .tablist button {
    text-align: left;
    padding: 7px 10px;
    border: 0;
    border-radius: var(--radius);
    background: none;
    color: var(--soft);
    cursor: pointer;
    font-size: 13px;
  }
  .tablist button:hover {
    background: var(--hover);
    color: var(--ink);
  }
  .tablist button[aria-current='page'] {
    background: var(--accent-soft);
    color: var(--accent-ink);
    font-weight: 500;
  }
</style>
