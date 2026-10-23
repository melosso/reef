<script lang="ts">
  import type { Snippet } from 'svelte';
  import { Search } from '@lucide/svelte';
  import { openCommandBar } from '../lib/command.svelte';
  import AccountMenu from './AccountMenu.svelte';

  interface Crumb {
    label: string;
    href?: string;
  }

  let {
    crumbs,
    status,
    actions,
    children,
  }: {
    crumbs: Crumb[];
    status?: Snippet;
    actions?: Snippet;
    children: Snippet;
  } = $props();

  $effect(() => {
    document.title = `${crumbs
      .map((c) => c.label)
      .reverse()
      .join(' · ')} · Reef`;
  });
</script>

<div class="page">
  <header class="head">
    <nav aria-label="Breadcrumb" class="crumbs">
      {#each crumbs as crumb, i}
        {#if i > 0}<span aria-hidden="true">/</span>{/if}
        {#if crumb.href && i < crumbs.length - 1}<a href={crumb.href}>{crumb.label}</a>
        {:else}<span class="current" aria-current={i === crumbs.length - 1 ? 'page' : undefined}>{crumb.label}</span>{/if}
      {/each}
    </nav>
    {#if status}<div class="status">{@render status()}</div>{/if}
    <div class="center">
      <button type="button" class="command" onclick={openCommandBar} aria-keyshortcuts="Control+K">
        <Search size={15} />
        <span>Search or run a command</span>
        <kbd>Ctrl K</kbd>
      </button>
    </div>
    {#if actions}<div class="actions">{@render actions()}</div>{/if}
    <AccountMenu />
  </header>
  {@render children()}
</div>

<style>
  .head {
    height: 56px;
    flex: none;
    display: flex;
    align-items: center;
    gap: 14px;
    padding: 0 20px;
    background: var(--surface);
    border-bottom: 1px solid var(--line);
  }
  .crumbs {
    display: flex;
    align-items: center;
    gap: 8px;
    font-size: 14px;
    color: var(--muted);
    white-space: nowrap;
    min-width: 0;
  }
  .crumbs a {
    color: var(--muted);
    text-decoration: none;
  }
  .crumbs a:hover {
    color: var(--ink);
  }
  .current {
    color: var(--ink);
    font-weight: 600;
    overflow: hidden;
    text-overflow: ellipsis;
  }
  .status {
    font-size: 13px;
    color: var(--muted);
    white-space: nowrap;
  }
  .center {
    flex: 1;
    display: flex;
    justify-content: center;
    min-width: 0;
  }
  .command {
    width: 300px;
    max-width: 100%;
    display: flex;
    align-items: center;
    gap: 10px;
    padding: 6px 10px;
    border: 1px solid var(--line-strong);
    border-radius: var(--radius);
    background: var(--bg);
    color: var(--muted);
    cursor: pointer;
    text-align: left;
  }
  .command span {
    flex: 1;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  kbd {
    font-family: var(--font);
    font-size: 12px;
  }
  .actions {
    display: flex;
    align-items: center;
    gap: 8px;
  }
  @media (max-width: 1100px) {
    .command span,
    kbd {
      display: none;
    }
    .command {
      width: auto;
    }
  }
</style>
