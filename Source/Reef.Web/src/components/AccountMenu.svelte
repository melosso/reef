<script lang="ts">
  import { LogOut, Moon, Sun, User } from '@lucide/svelte';
  import { displayName, session } from '../lib/session.svelte';
  import { isDark, setTheme } from '../lib/theme.svelte';
  import { navigate } from '../lib/router.svelte';
  import { tooltip } from '../lib/tooltip';

  let menu: HTMLElement;
  const initials = $derived(
    displayName()
      .split(/\s+/)
      .map((p) => p[0])
      .join('')
      .slice(0, 2)
      .toUpperCase(),
  );
  const toggleLabel = $derived(isDark() ? 'Switch to light theme' : 'Switch to dark theme');

  function go(path: string) {
    menu.hidePopover();
    navigate(path);
  }
</script>

<button
  type="button"
  class="btn ghost icon"
  aria-label={toggleLabel}
  use:tooltip={toggleLabel}
  onclick={() => setTheme(isDark() ? 'light' : 'dark')}
  >{#if isDark()}<Sun size={16} />{:else}<Moon size={16} />{/if}</button
>
<button type="button" class="avatar" popovertarget="account-menu" aria-label="Account menu">{initials}</button>
<div id="account-menu" popover bind:this={menu} class="menu">
  <div class="who">
    <strong>{displayName()}</strong>
    <span class="muted">{session.username} · {session.role}</span>
  </div>
  <button type="button" onclick={() => go('/account')}><User size={15} />Account settings</button>
  <button type="button" onclick={() => go('/logout')}><LogOut size={15} />Sign out</button>
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
</style>
