<script lang="ts">
  import { onMount } from 'svelte';
  import Logo from '../components/Logo.svelte';
  import { clearSession } from '../lib/session.svelte';
  import { navigate } from '../lib/router.svelte';

  onMount(() => {
    fetch('/api/auth/logout', { method: 'POST', credentials: 'same-origin' }).catch(() => {});
    clearSession();
    const timer = setTimeout(() => navigate('/login', true), 1200);
    return () => clearTimeout(timer);
  });
</script>

<svelte:head><title>Signed out · Reef</title></svelte:head>

<div class="wrap">
  <Logo size={28} />
  <h1>Signed out</h1>
  <a href="/login">Sign in again</a>
</div>

<style>
  .wrap {
    min-height: 100%;
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    gap: 12px;
  }
  h1 {
    font-size: 18px;
  }
</style>
