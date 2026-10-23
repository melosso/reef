<script lang="ts">
  import { SlidersHorizontal } from '@lucide/svelte';
  import { navItems } from '../lib/nav';
  import { route } from '../lib/router.svelte';
  import { recent } from '../lib/recent.svelte';
  import { isAdmin } from '../lib/session.svelte';
  import { api } from '../lib/api';
  import StatusMark from './StatusMark.svelte';
  import Logo from './Logo.svelte';

  let approvals = $state(0);

  async function poll() {
    approvals = await api
      .get<{ count: number }>('/api/email-approvals/count')
      .then((r) => r?.count ?? 0)
      .catch(() => 0);
  }

  $effect(() => {
    poll();
    const timer = setInterval(poll, 60000);
    return () => clearInterval(timer);
  });

  const active = (href: string) => route.path === href || route.path.startsWith(`${href}/`);
</script>

<aside class="side">
  <a class="brand" href="/dashboard"><Logo /><span>Reef</span></a>
  <nav aria-label="Main">
    {#each navItems as item}
      <a href={item.href} aria-current={active(item.href) ? 'page' : undefined}>
        <item.icon size={16} strokeWidth={1.75} />
        <span class="label">{item.label}</span>
        {#if item.href === '/email-approvals' && approvals > 0}<span class="num count" aria-label="{approvals} pending">{approvals}</span
          >{/if}
      </a>
    {/each}
  </nav>
  {#if recent.items.length}
    <div class="recent">
      <h2>Recent</h2>
      {#each recent.items as item (item.kind + item.id)}
        <a href="/profiles/{item.kind}/{item.id}" aria-current={route.path === `/profiles/${item.kind}/${item.id}` ? 'page' : undefined}>
          <StatusMark kind={item.status ?? 'idle'} />
          <span class="label">{item.name}</span>
        </a>
      {/each}
    </div>
  {/if}
  <div class="grow"></div>
  {#if isAdmin()}
    <a href="/admin" aria-current={active('/admin') ? 'page' : undefined}
      ><SlidersHorizontal size={16} strokeWidth={1.75} /><span class="label">Admin</span></a
    >
  {/if}
</aside>

<style>
  .side {
    width: 240px;
    flex: none;
    display: flex;
    flex-direction: column;
    gap: 2px;
    padding: 12px;
    background: var(--surface);
    border-right: 1px solid var(--line);
    overflow-y: auto;
  }
  .brand {
    display: flex;
    align-items: center;
    gap: 10px;
    padding: 8px 12px 18px;
    font-size: 16px;
    font-weight: 600;
    color: var(--ink);
    text-decoration: none;
  }
  nav,
  .recent {
    display: flex;
    flex-direction: column;
    gap: 2px;
  }
  a:not(.brand) {
    display: flex;
    align-items: center;
    gap: 12px;
    padding: 7px 12px;
    border-radius: var(--radius);
    color: var(--soft);
    text-decoration: none;
    font-size: 14px;
  }
  a:not(.brand):hover {
    background: var(--hover);
    color: var(--ink);
  }
  a[aria-current='page'] {
    background: var(--accent-soft);
    color: var(--accent-ink);
    font-weight: 500;
  }
  .label {
    flex: 1;
    min-width: 0;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  .count {
    color: var(--accent-ink);
  }
  .recent {
    margin-top: 20px;
  }
  .recent h2 {
    font-size: 12px;
    font-weight: 500;
    color: var(--muted);
    padding: 0 12px 6px;
  }
  .recent a {
    font-size: 13px;
  }
  .grow {
    flex: 1;
    min-height: 16px;
  }
</style>
