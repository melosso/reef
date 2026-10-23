<script lang="ts">
  import { pending, settle } from '../lib/confirm.svelte';

  let dialog: HTMLDialogElement;
  let typed = $state('');

  $effect(() => {
    if (pending.request && !dialog.open) {
      typed = '';
      dialog.showModal();
    } else if (!pending.request && dialog.open) {
      dialog.close();
    }
  });

  const blocked = $derived(!!pending.request?.typed && typed !== pending.request.typed);
</script>

<dialog bind:this={dialog} onclose={() => settle(false)} aria-labelledby="confirm-title">
  {#if pending.request}
    <form
      method="dialog"
      onsubmit={(e) => {
        e.preventDefault();
        if (!blocked) settle(true);
      }}
    >
      <div class="body">
        <h2 id="confirm-title">{pending.request.title}</h2>
        {#if pending.request.message}<p>{pending.request.message}</p>{/if}
        {#if pending.request.typed}
          <div class="field">
            <label for="confirm-typed">Type <strong class="num">{pending.request.typed}</strong> to confirm</label>
            <input id="confirm-typed" bind:value={typed} autocomplete="off" spellcheck="false" />
          </div>
        {/if}
      </div>
      <div class="foot">
        <button type="button" class="btn" onclick={() => settle(false)}>Cancel</button>
        <button type="submit" class="btn primary" class:danger-fill={pending.request.danger} disabled={blocked}
          >{pending.request.confirm ?? 'Confirm'}</button
        >
      </div>
    </form>
  {/if}
</dialog>

<style>
  .body {
    padding: 20px;
    display: flex;
    flex-direction: column;
    gap: 12px;
    min-width: 360px;
  }
  .body p {
    color: var(--soft);
  }
  .foot {
    display: flex;
    justify-content: flex-end;
    gap: 8px;
    padding: 12px 20px;
    border-top: 1px solid var(--line);
  }
  .danger-fill {
    background: var(--err);
    border-color: var(--err);
    color: var(--surface);
  }
</style>
