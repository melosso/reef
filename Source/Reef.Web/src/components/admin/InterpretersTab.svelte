<script lang="ts">
  import StatusMark from '../StatusMark.svelte';
  import { api } from '../../lib/api';
  import type { Interpreter } from '../../models/Interpreter';
  import { notify } from '../../lib/notify';

  let list = $state<Interpreter[]>([]);
  let busy = $state<string | null>(null);

  async function load() {
    list = await api.get<Interpreter[]>('/api/profiles/interpreters').catch((e) => {
      notify.fail(e);
      return [];
    });
  }

  $effect(() => {
    load();
  });

  async function test(name: string) {
    busy = name;
    try {
      const s = await api.post<Interpreter>(`/api/profiles/interpreters/${name}/test`);
      (s.available ? notify.success : notify.error)(`${s.displayName} is ${s.available ? 'available' : 'not available'}`);
      await load();
    } catch (e) {
      notify.fail(e);
    } finally {
      busy = null;
    }
  }
</script>

<p class="muted">Interpreters run pre-processing and post-processing scripts on this host.</p>
<div class="table-wrap">
  <table class="data">
    <thead><tr><th>Interpreter</th><th>Status</th><th>Details</th><th><span class="sr">Actions</span></th></tr></thead>
    <tbody>
      {#each list as i (i.name)}
        <tr>
          <td class="strong">{i.displayName ?? i.name}</td>
          <td><StatusMark kind={i.available ? 'ok' : 'off'} label={i.available ? 'Available' : 'Not available'} /></td>
          <td class="muted">{i.available ? i.output || '' : i.error || 'Not installed on this host'}</td>
          <td class="actions"><button type="button" class="btn" aria-busy={busy === i.name} onclick={() => test(i.name)}>Test</button></td>
        </tr>
      {:else}
        <tr><td colspan="4" class="empty">No interpreters</td></tr>
      {/each}
    </tbody>
  </table>
</div>

<style>
  .strong {
    font-weight: 500;
  }
</style>
