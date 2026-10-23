<script lang="ts">
  import { LogOut, Monitor, Moon, Sun, User } from '@lucide/svelte';
  import { displayName, session } from '../lib/session.svelte';
  import { setTheme, theme, type ThemeChoice } from '../lib/theme.svelte';
  import { navigate } from '../lib/router.svelte';

  let menu: HTMLElement;
  const initials = $derived(
    displayName()
      .split(/\s+/)
      .map((p) => p[0])
      .join('')
      .slice(0, 2)
      .toUpperCase(),
  );
  const choices: { value: ThemeChoice; label: string; icon: typeof Sun }[] = [
    { value: 'system', label: 'System', icon: Monitor },
    { value: 'light', label: 'Light', icon: Sun },
    { value: 'dark', label: 'Dark', icon: Moon },
  ];

  function go(path: string) {
    menu.hidePopover();
    navigate(path);
  }
</script>

<button type="button" class="avatar" popovertarget="account-menu" aria-label="Account menu">{initials}</button>
<div id="account-menu" popover bind:this={menu} class="menu">
  <div class="who">
    <strong>{displayName()}</strong>
    <span class="muted">{session.username} · {session.role}</span>
  </div>
  <button type="button" onclick={() => go('/account')}><User size={15} />Account settings</button>
  <div class="theme" role="radiogroup" aria-label="Theme">
    {#each choices as choice}
      <button type="button" role="radio" aria-checked={theme.choice === choice.value} onclick={() => setTheme(choice.value)}>
        <choice.icon size={14} />{choice.label}
      </button>
    {/each}
  </div>
  <button type="button" onclick={() => go('/logoff')}><LogOut size={15} />Sign out</button>
</div>

<style>
  .avatar {
    width: 32px;
    height: 32px;
    flex: none;
    border: 1px solid var(--line-strong);
    border-radius: var(--radius);
    background: var(--hover);
    color: var(--soft);
    font-size: 12px;
    font-weight: 600;
    cursor: pointer;
  }
  .menu {
    position: fixed;
    inset: 52px 16px auto auto;
    margin: 0;
    width: 240px;
    padding: 6px;
    border: 1px solid var(--line-strong);
    border-radius: var(--radius);
    background: var(--surface);
    color: var(--ink);
    box-shadow: 0 8px 24px var(--shadow);
  }
  .who {
    display: flex;
    flex-direction: column;
    gap: 2px;
    padding: 8px 10px 10px;
    border-bottom: 1px solid var(--line);
    margin-bottom: 4px;
  }
  .who span {
    font-size: 12px;
  }
  .menu > button {
    display: flex;
    align-items: center;
    gap: 10px;
    width: 100%;
    padding: 8px 10px;
    border: 0;
    border-radius: var(--radius);
    background: none;
    cursor: pointer;
    text-align: left;
  }
  .menu > button:hover {
    background: var(--hover);
  }
  .theme {
    display: grid;
    grid-template-columns: repeat(3, minmax(0, 1fr));
    gap: 2px;
    padding: 4px;
    margin: 4px 0;
    border: 1px solid var(--line);
    border-radius: var(--radius);
  }
  .theme button {
    display: flex;
    align-items: center;
    justify-content: center;
    gap: 4px;
    padding: 5px 0;
    border: 0;
    border-radius: var(--radius);
    background: none;
    font-size: 12px;
    color: var(--soft);
    cursor: pointer;
  }
  .theme button[aria-checked='true'] {
    background: var(--accent-soft);
    color: var(--accent-ink);
    font-weight: 500;
  }
</style>
