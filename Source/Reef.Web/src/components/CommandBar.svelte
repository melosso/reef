<script lang="ts">
  import type { LucideIcon } from '@lucide/svelte';
  import { LogOut, Moon, Play, Plus, SlidersHorizontal, Sun, User, Workflow } from '@lucide/svelte';
  import { commandBar } from '../lib/command.svelte';
  import { navItems } from '../lib/nav';
  import { navigate } from '../lib/router.svelte';
  import { isAdmin } from '../lib/session.svelte';
  import { setTheme } from '../lib/theme.svelte';
  import { api } from '../lib/api';
  import type { ExportProfile } from '../models/ExportProfile';
  import type { ImportProfile } from '../models/ImportProfile';
  import { notify } from '../lib/notify';
  import { runProfile } from '../lib/profiles';

  interface Command {
    id: string;
    label: string;
    group: string;
    icon: LucideIcon;
    run: () => void | Promise<void>;
  }

  let dialog: HTMLDialogElement;
  let query = $state('');
  let index = $state(0);
  let profiles = $state<{ kind: 'export' | 'import'; id: number; name: string }[]>([]);

  async function loadProfiles() {
    const [exports, imports] = await Promise.all([
      api.get<ExportProfile[]>('/api/profiles').catch(() => []),
      api.get<ImportProfile[]>('/api/profiles?type=import').catch(() => []),
    ]);
    profiles = [
      ...(exports ?? []).map((p) => ({ kind: 'export' as const, id: p.id, name: p.name })),
      ...(imports ?? []).map((p) => ({ kind: 'import' as const, id: p.id, name: p.name })),
    ];
  }

  const commands = $derived<Command[]>([
    ...navItems.map((n) => ({ id: n.href, label: n.label, group: 'Go to', icon: n.icon, run: () => navigate(n.href) })),
    ...(isAdmin() ? [{ id: '/admin', label: 'Admin', group: 'Go to', icon: SlidersHorizontal, run: () => navigate('/admin') }] : []),
    { id: '/account', label: 'Account settings', group: 'Go to', icon: User, run: () => navigate('/account') },
    { id: 'new-export', label: 'New export profile', group: 'Create', icon: Plus, run: () => navigate('/profiles/export/new') },
    { id: 'new-import', label: 'New import profile', group: 'Create', icon: Plus, run: () => navigate('/profiles/import/new') },
    ...profiles.map((p) => ({
      id: `open-${p.kind}-${p.id}`,
      label: p.name,
      group: p.kind === 'export' ? 'Export profiles' : 'Import profiles',
      icon: Workflow,
      run: () => navigate(`/profiles/${p.kind}/${p.id}`),
    })),
    ...profiles.map((p) => ({
      id: `run-${p.kind}-${p.id}`,
      label: `Run ${p.name}`,
      group: 'Run',
      icon: Play,
      run: () => runProfile(p.kind, p.id, p.name).catch((e) => notify.fail(e)),
    })),
    { id: 'theme-light', label: 'Use light theme', group: 'Theme', icon: Sun, run: () => setTheme('light') },
    { id: 'theme-dark', label: 'Use dark theme', group: 'Theme', icon: Moon, run: () => setTheme('dark') },
    { id: 'sign-out', label: 'Sign out', group: 'Account', icon: LogOut, run: () => navigate('/logoff') },
  ]);

  const results = $derived.by(() => {
    const terms = query.trim().toLowerCase().split(/\s+/).filter(Boolean);
    const list = terms.length
      ? commands.filter((c) => terms.every((t) => `${c.group} ${c.label}`.toLowerCase().includes(t)))
      : commands.filter((c) => !c.id.startsWith('run-') && !c.id.startsWith('open-'));
    return list.slice(0, 40);
  });

  $effect(() => {
    if (commandBar.open && !dialog.open) {
      query = '';
      index = 0;
      dialog.showModal();
      loadProfiles();
    } else if (!commandBar.open && dialog.open) {
      dialog.close();
    }
  });

  function execute(command: Command | undefined) {
    if (!command) return;
    commandBar.open = false;
    command.run();
  }

  function onkeydown(e: KeyboardEvent) {
    if (e.key === 'ArrowDown') {
      e.preventDefault();
      index = Math.min(results.length - 1, index + 1);
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      index = Math.max(0, index - 1);
    } else if (e.key === 'Enter') {
      e.preventDefault();
      execute(results[index]);
    }
  }

  function onglobalkey(e: KeyboardEvent) {
    if ((e.ctrlKey || e.metaKey) && e.key.toLowerCase() === 'k') {
      e.preventDefault();
      commandBar.open = !commandBar.open;
    }
  }
</script>

<svelte:window onkeydown={onglobalkey} />

<dialog
  bind:this={dialog}
  class="bar"
  onclose={() => (commandBar.open = false)}
  onclick={(e) => {
    if (e.target === dialog) commandBar.open = false;
  }}
  aria-label="Command bar"
>
  <input
    type="search"
    placeholder="Search pages, profiles and commands"
    aria-label="Search pages, profiles and commands"
    role="combobox"
    aria-expanded="true"
    aria-controls="command-results"
    aria-activedescendant={results[index] ? `command-${index}` : undefined}
    bind:value={query}
    oninput={() => (index = 0)}
    {onkeydown}
  />
  <ul id="command-results" role="listbox">
    {#each results as command, i (command.id)}
      {#if i === 0 || results[i - 1].group !== command.group}<li class="group" role="presentation">{command.group}</li>{/if}
      <li
        id="command-{i}"
        role="option"
        aria-selected={i === index}
        onmousemove={() => (index = i)}
        onclick={() => execute(command)}
        onkeydown={() => {}}
      >
        <command.icon size={15} />{command.label}
      </li>
    {:else}
      <li class="none" role="presentation">No matches</li>
    {/each}
  </ul>
</dialog>

<style>
  .bar {
    width: 560px;
    max-width: 92vw;
    margin-top: 12vh;
  }
  input {
    border: 0 !important;
    border-bottom: 1px solid var(--line) !important;
    border-radius: 0 !important;
    padding: 14px 16px !important;
    font-size: 14px !important;
    outline: none !important;
  }
  ul {
    list-style: none;
    margin: 0;
    padding: 6px;
    max-height: 56vh;
    overflow: auto;
  }
  li[role='option'] {
    display: flex;
    align-items: center;
    gap: 10px;
    padding: 8px 10px;
    border-radius: var(--radius);
    cursor: pointer;
    color: var(--soft);
  }
  li[aria-selected='true'] {
    background: var(--accent-soft);
    color: var(--accent-ink);
  }
  .group {
    padding: 10px 10px 4px;
    font-size: 12px;
    color: var(--muted);
  }
  .none {
    padding: 16px 10px;
    color: var(--muted);
  }
</style>
