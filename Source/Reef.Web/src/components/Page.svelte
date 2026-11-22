<script lang="ts">
  import type { Snippet } from 'svelte';
  import { Search } from '@lucide/svelte';
  import { openCommandBar } from '../lib/command.svelte';
  import AccountMenu from './AccountMenu.svelte';
  import { tooltip } from '../lib/tooltip';

  interface Crumb {
    label: string;
    href?: string;
    value?: string;
    onrename?: (name: string) => void;
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

  let renaming = $state(false);

  function commit(crumb: Crumb, input: HTMLInputElement) {
    if (!renaming) return;
    renaming = false;
    if (input.value !== (crumb.value ?? crumb.label)) crumb.onrename?.(input.value);
  }

  function autofocus(input: HTMLInputElement) {
    input.focus();
    input.select();
  }

  $effect(() => {
    document.title = `${crumbs
      .map((c) => c.label)
      .reverse()
      .join(' · ')} · Reef`;
  });
</script>

<div class="page">
  <header class="head">
    <div class="left">
      <nav aria-label="Breadcrumb" class="crumbs">
        {#each crumbs as crumb, i (i)}
          {#if i > 0}<span aria-hidden="true">/</span>{/if}
          {#if crumb.href && i < crumbs.length - 1}<a href={crumb.href}>{crumb.label}</a>
          {:else if crumb.onrename && renaming}<input
              class="rename"
              aria-label="Name"
              value={crumb.value ?? crumb.label}
              use:autofocus
              onblur={(e) => commit(crumb, e.currentTarget)}
              onkeydown={(e) => {
                if (e.key === 'Enter') commit(crumb, e.currentTarget);
                else if (e.key === 'Escape') renaming = false;
              }}
            />
          {:else if crumb.onrename}<h1 class="title">
              <span
                class="current editable"
                role="button"
                tabindex="0"
                aria-current="page"
                aria-keyshortcuts="F2"
                use:tooltip={'Double-click to rename'}
                ondblclick={() => (renaming = true)}
                onkeydown={(e) => {
                  if (e.key === 'F2') renaming = true;
                }}
              >
                {crumb.label}
              </span>
            </h1>
          {:else if i === crumbs.length - 1}<h1 class="title"><span class="current" aria-current="page">{crumb.label}</span></h1>
          {:else}<span class="current">{crumb.label}</span>{/if}
        {/each}
      </nav>
      {#if status}<div class="status">{@render status()}</div>{/if}
    </div>
    <div class="center">
      <button
        type="button"
        class="command"
        onclick={openCommandBar}
        aria-keyshortcuts="Control+K"
        aria-label="Search or run a command"
        use:tooltip={'Search or run a command (Ctrl K)'}
      >
        <Search size={15} />
        <span>Search or run a command</span>
        <kbd>Ctrl K</kbd>
      </button>
    </div>
    <div class="right">
      {#if actions}<div class="actions">{@render actions()}</div>{/if}
      <AccountMenu />
    </div>
  </header>
  {@render children()}
</div>

<style>
  .head {
    height: 56px;
    flex: none;
    display: grid;
    grid-template-columns: minmax(0, 1fr) auto minmax(0, 1fr);
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
    flex: 0 1 auto;
    overflow: hidden;
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
  .editable {
    cursor: text;
  }
  .title {
    display: flex;
    min-width: 0;
    font-size: inherit;
  }
  .rename {
    font: inherit;
    font-weight: 600;
    min-width: 240px;
  }
  .status {
    flex: none;
    font-size: 13px;
    color: var(--muted);
    white-space: nowrap;
  }
  .left,
  .right {
    display: flex;
    align-items: center;
    gap: 14px;
    min-width: 0;
  }
  .right {
    justify-content: flex-end;
  }
  .center {
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
      flex: none;
    }
  }
  @media (max-width: 900px) {
    .crumbs a,
    .crumbs > span[aria-hidden='true'] {
      display: none;
    }
  }
</style>
