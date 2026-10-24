<script lang="ts">
  import { Check, ChevronLeft, ChevronRight, Eye, Paperclip, SkipForward, X } from '@lucide/svelte';
  import Page from '../components/Page.svelte';
  import Sheet from '../components/Sheet.svelte';
  import StatusMark from '../components/StatusMark.svelte';
  import { api } from '../lib/api';
  import type { ApprovalAttachment } from '../models/ApprovalAttachment';
  import type { AttachmentConfig } from '../models/AttachmentConfig';
  import type { EmailApproval } from '../models/EmailApproval';
  import type { EmailPreview } from '../models/EmailPreview';
  import type { ExportProfile } from '../models/ExportProfile';
  import type { PagedResult } from '../models/PagedResult';
  import { formatBytes, formatDateTime } from '../lib/format';
  import { notify } from '../lib/notify';
  import { match, navigate, route } from '../lib/router.svelte';
  import { urlPaging } from '../lib/paging.svelte';
  import { SvelteSet } from 'svelte/reactivity';
  import { statusKind, statusLabel } from '../lib/status';
  import { tooltip } from '../lib/tooltip';

  type Tab = 'pending' | 'approved' | 'history';
  type Action = 'approve' | 'reject' | 'skip';

  const TABS: Tab[] = ['pending', 'approved', 'history'];
  const tab = $derived(TABS.find((t) => t === match('/approvals/:tab', route.path)?.tab) ?? 'pending');
  const paging = urlPaging(25);

  let approvals = $state<EmailApproval[]>([]);
  let profiles = $state<ExportProfile[]>([]);
  let loaded = $state(false);
  const selected = new SvelteSet<string>();
  let preview = $state<EmailPreview | null>(null);
  let action = $state<{ kind: Action; guids: string[] } | null>(null);
  let note = $state('');
  let busy = $state(false);
  let dialog: HTMLDialogElement;

  async function load() {
    const list = await api.get<PagedResult<EmailApproval>>('/api/email-approvals?pageSize=1000').catch((e) => {
      notify.fail(e, 'approvals');
      return { data: approvals } as PagedResult<EmailApproval>;
    });
    approvals = list?.data ?? [];
    loaded = true;
  }

  $effect(() => {
    api
      .get<ExportProfile[]>('/api/profiles')
      .then((p) => (profiles = p ?? []))
      .catch(() => {});
    load();
    const timer = setInterval(load, 10000);
    return () => clearInterval(timer);
  });

  const lists = $derived({
    pending: approvals.filter((a) => a.status === 'Pending'),
    approved: approvals.filter((a) => a.status === 'Approved'),
    history: approvals.filter((a) => ['Sent', 'Failed', 'Rejected', 'Skipped'].includes(a.status)),
  });
  const current = $derived(lists[tab]);
  const pages = $derived(Math.max(1, Math.ceil(current.length / paging.size)));
  const pageItems = $derived(current.slice((paging.page - 1) * paging.size, paging.page * paging.size));
  const allOnPage = $derived(pageItems.length > 0 && pageItems.every((a) => selected.has(a.guid)));
  const someOnPage = $derived(pageItems.some((a) => selected.has(a.guid)));

  const profileName = (id: string) => profiles.find((p) => p.id === id)?.name ?? `Profile ${id}`;
  const redacted = (v: string | null | undefined) => v === '[REDACTED]';

  function attachments(config: string | null | undefined): ApprovalAttachment[] {
    if (!config || config === 'null') return [];
    try {
      const c: AttachmentConfig = JSON.parse(config);
      return c.attachments || c.generatedAttachments || c.GeneratedAttachments || [];
    } catch {
      return [];
    }
  }

  function hasAttachments(config: string | null | undefined): boolean {
    if (!config || config === 'null') return false;
    try {
      const c: AttachmentConfig = JSON.parse(config);
      return (c.generatedAttachments || c.GeneratedAttachments || []).length > 0 || !!((c.enabled ?? c.Enabled) && (c.mode ?? c.Mode));
    } catch {
      return false;
    }
  }

  function toggle(guid: string) {
    if (selected.has(guid)) selected.delete(guid);
    else selected.add(guid);
  }

  function toggleAll() {
    const clear = allOnPage;
    for (const a of pageItems) {
      if (clear) selected.delete(a.guid);
      else selected.add(a.guid);
    }
  }

  async function openPreview(guid: string) {
    try {
      const data = await api.get<EmailPreview>(`/api/email-approvals/${guid}/preview`);
      preview = { ...data, guid, status: approvals.find((a) => a.guid === guid)?.status ?? data?.status };
    } catch (e) {
      notify.fail(e);
    }
  }

  function showTab(next: Tab | null) {
    navigate(next ? `/approvals/${next}` : '/approvals');
  }

  function ask(kind: Action, guids: string[]) {
    action = { kind, guids };
    note = '';
    dialog.showModal();
  }

  const ACTION_TEXT: Record<Action, { title: string; label: string; field: string; confirm: string }> = {
    approve: { title: 'Approve and send', label: 'Notes', field: 'notes', confirm: 'Approve and send' },
    reject: { title: 'Reject', label: 'Reason', field: 'reason', confirm: 'Reject' },
    skip: { title: 'Skip', label: 'Notes', field: 'notes', confirm: 'Skip' },
  };

  async function submit(e: SubmitEvent) {
    e.preventDefault();
    if (!action) return;
    busy = true;
    const { kind, guids } = action;
    const text = ACTION_TEXT[kind];
    let ok = 0;
    let failed = 0;
    for (const guid of guids) {
      try {
        await api.post(`/api/email-approvals/${guid}/${kind}`, { [text.field]: note.trim() || (guids.length > 1 ? `Bulk ${kind}` : null) });
        ok++;
        selected.delete(guid);
      } catch {
        failed++;
      }
    }
    busy = false;
    dialog.close();
    if (preview && guids.includes(preview.guid)) preview = null;
    const verb = kind === 'approve' ? 'approved' : kind === 'reject' ? 'rejected' : 'skipped';
    if (ok) notify.success(`${ok} ${ok === 1 ? 'email' : 'emails'} ${verb}`);
    if (failed) notify.error(`${failed} ${failed === 1 ? 'email' : 'emails'} could not be ${verb}.`);
    await load();
  }
</script>

<Page crumbs={[{ label: 'Approvals' }]}>
  <div class="page-body">
    <div class="tabs" role="tablist">
      <button type="button" role="tab" aria-selected={tab === 'pending'} onclick={() => showTab(null)}
        >Pending <span class="num">{lists.pending.length}</span></button
      >
      <button type="button" role="tab" aria-selected={tab === 'approved'} onclick={() => showTab('approved')}
        >Approved <span class="num">{lists.approved.length}</span></button
      >
      <button type="button" role="tab" aria-selected={tab === 'history'} onclick={() => showTab('history')}
        >History <span class="num">{lists.history.length}</span></button
      >
    </div>

    {#if tab === 'pending' && selected.size}
      <div class="toolbar bulk">
        <span><span class="num">{selected.size}</span> selected</span>
        <button type="button" class="btn primary" onclick={() => ask('approve', [...selected])}><Check size={14} />Approve and send</button>
        <button type="button" class="btn danger" onclick={() => ask('reject', [...selected])}><X size={14} />Reject</button>
        <button type="button" class="link" onclick={() => selected.clear()}>Clear selection</button>
      </div>
    {/if}

    <div class="table-wrap">
      <table class="data">
        <thead>
          <tr>
            {#if tab === 'pending'}<th class="sel"
                ><input
                  type="checkbox"
                  aria-label="Select all on this page"
                  checked={allOnPage}
                  indeterminate={someOnPage && !allOnPage}
                  onchange={toggleAll}
                /></th
              >{/if}
            <th>Profile</th>
            {#if tab === 'history'}<th>Status</th>{/if}
            <th>Recipients</th>
            {#if tab !== 'history'}<th>Subject</th>{/if}
            {#if tab === 'pending'}<th><span class="sr">Attachments</span></th>{/if}
            <th>{tab === 'approved' ? 'Approved' : 'Created'}</th>
            {#if tab === 'history'}<th>Sent</th>{/if}
            {#if tab === 'pending'}<th><span class="sr">Actions</span></th>{/if}
          </tr>
        </thead>
        <tbody>
          {#each pageItems as a (a.guid)}
            <tr
              class="clickable"
              onclick={(e) => {
                if (!(e.target as Element).closest('button, input')) openPreview(a.guid);
              }}
            >
              {#if tab === 'pending'}<td class="sel"
                  ><input type="checkbox" aria-label="Select approval" checked={selected.has(a.guid)} onchange={() => toggle(a.guid)} /></td
                >{/if}
              <td class="strong">{profileName(a.profileId)}</td>
              {#if tab === 'history'}<td><StatusMark kind={statusKind(a.status)} label={statusLabel(a.status)} /></td>{/if}
              <td class="trunc"
                >{#if redacted(a.recipients)}<span class="muted">Redacted</span>{:else}{a.recipients}{/if}</td
              >
              {#if tab !== 'history'}<td class="trunc"
                  >{#if redacted(a.subject)}<span class="muted">Redacted</span>{:else}{a.subject}{/if}</td
                >{/if}
              {#if tab === 'pending'}<td
                  >{#if hasAttachments(a.attachmentConfig)}<span use:tooltip={'Has attachments'}><Paperclip size={14} /></span>{/if}</td
                >{/if}
              <td>{formatDateTime(tab === 'approved' ? a.approvedAt : a.createdAt)}</td>
              {#if tab === 'history'}<td>{a.sentAt ? formatDateTime(a.sentAt) : a.status === 'Failed' ? 'Failed' : 'Not sent'}</td>{/if}
              {#if tab === 'pending'}
                <td class="actions">
                  <button
                    type="button"
                    class="btn ghost icon"
                    aria-label="Preview"
                    use:tooltip={'Preview'}
                    onclick={() => openPreview(a.guid)}><Eye size={15} /></button
                  >
                  <button
                    type="button"
                    class="btn ghost icon"
                    aria-label="Approve"
                    use:tooltip={'Approve and send'}
                    onclick={() => ask('approve', [a.guid])}><Check size={15} /></button
                  >
                  <button
                    type="button"
                    class="btn ghost icon"
                    aria-label="Skip"
                    use:tooltip={'Skip. Commits the sync hash without sending.'}
                    onclick={() => ask('skip', [a.guid])}><SkipForward size={15} /></button
                  >
                  <button
                    type="button"
                    class="btn ghost icon danger"
                    aria-label="Reject"
                    use:tooltip={'Reject'}
                    onclick={() => ask('reject', [a.guid])}><X size={15} /></button
                  >
                </td>
              {/if}
            </tr>
          {:else}
            <tr
              ><td colspan="8" class="empty"
                >{!loaded
                  ? 'Loading approvals'
                  : tab === 'pending'
                    ? 'No emails wait for approval.'
                    : tab === 'approved'
                      ? 'No approved emails wait for sending.'
                      : 'No history'}</td
              ></tr
            >
          {/each}
        </tbody>
      </table>
    </div>

    {#if pages > 1}
      <div class="pager">
        <span class="spacer"></span>
        <button type="button" class="btn icon" aria-label="Previous page" disabled={paging.page <= 1} onclick={() => paging.page--}
          ><ChevronLeft size={15} /></button
        >
        <span class="num">{paging.page} / {pages}</span>
        <button type="button" class="btn icon" aria-label="Next page" disabled={paging.page >= pages} onclick={() => paging.page++}
          ><ChevronRight size={15} /></button
        >
      </div>
    {/if}
  </div>
</Page>

{#if preview}
  <Sheet title="Email preview" onclose={() => (preview = null)} wide>
    <dl class="meta">
      <dt>Subject</dt>
      <dd>{redacted(preview.subject) ? 'Redacted' : preview.subject}</dd>
      <dt>To</dt>
      <dd>{redacted(preview.recipients) ? 'Redacted' : preview.recipients}</dd>
      <dt>Cc</dt>
      <dd>{redacted(preview.ccAddresses) ? 'Redacted' : preview.ccAddresses || 'None'}</dd>
    </dl>
    {#if attachments(preview.attachmentConfig).length}
      <details>
        <summary><span class="num">{attachments(preview.attachmentConfig).length}</span> attachments</summary>
        <ul class="files">
          {#each attachments(preview.attachmentConfig) as att, i (i)}
            {@const size = att.fileSize || att.FileSize || Math.floor(((att.contentBase64 || att.ContentBase64 || '').length * 3) / 4)}
            <li>
              <Paperclip size={13} />{att.fileName || att.filename || att.Filename || `Attachment ${i + 1}`}
              <span class="muted num">{formatBytes(size)}</span>
              <span class="muted">{att.contentType || att.mimeType || att.ContentType || ''}</span>
            </li>
          {/each}
        </ul>
      </details>
    {/if}
    {#if redacted(preview.htmlBody)}
      <p class="muted">Content redacted</p>
    {:else}
      <iframe class="frame" sandbox="" title="Email body" srcdoc={preview.htmlBody}></iframe>
    {/if}
    {#snippet footer()}
      {#if preview?.status === 'Pending'}
        {@const guid = preview.guid}
        <button type="button" class="btn danger" onclick={() => ask('reject', [guid])}>Reject</button>
        <button type="button" class="btn" onclick={() => ask('skip', [guid])}>Skip</button>
        <button type="button" class="btn primary" onclick={() => ask('approve', [guid])}>Approve and send</button>
      {:else}
        <button type="button" class="btn" onclick={() => (preview = null)}>Close</button>
      {/if}
    {/snippet}
  </Sheet>
{/if}

<dialog bind:this={dialog} aria-labelledby="act-title">
  {#if action}
    <form onsubmit={submit}>
      <div class="dbody fields">
        <h2 id="act-title">
          {ACTION_TEXT[action.kind].title}{#if action.guids.length > 1}
            <span class="num">{action.guids.length}</span> emails{/if}
        </h2>
        {#if action.kind === 'skip'}<p class="muted">The email is not sent. The sync hash is committed to prevent duplicates.</p>{/if}
        {#if action.kind === 'approve'}<p class="muted">Approved emails are sent to their recipients.</p>{/if}
        <div class="field">
          <label for="act-note">{ACTION_TEXT[action.kind].label}</label><textarea id="act-note" rows="3" bind:value={note}></textarea>
        </div>
      </div>
      <div class="dfoot">
        <button type="button" class="btn" onclick={() => dialog.close()}>Cancel</button>
        <button type="submit" class="btn primary" aria-busy={busy}>{ACTION_TEXT[action.kind].confirm}</button>
      </div>
    </form>
  {/if}
</dialog>

<style>
  .strong {
    font-weight: 500;
  }
  .trunc {
    max-width: 260px;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  .sel {
    width: 1%;
  }
  .bulk {
    padding: 8px 12px;
    background: var(--accent-soft);
    border-radius: var(--radius);
  }
  .pager {
    display: flex;
    align-items: center;
    gap: 8px;
  }
  .spacer {
    flex: 1;
  }
  .tabs .num {
    color: var(--muted);
    margin-left: 4px;
  }
  .meta {
    display: grid;
    grid-template-columns: 80px minmax(0, 1fr);
    gap: 6px 12px;
    margin: 0;
  }
  .meta dt {
    color: var(--muted);
  }
  .meta dd {
    margin: 0;
    word-break: break-word;
  }
  .files {
    list-style: none;
    padding: 0;
    margin: 8px 0 0;
    display: flex;
    flex-direction: column;
    gap: 6px;
  }
  .files li {
    display: flex;
    align-items: center;
    gap: 8px;
  }
  summary {
    cursor: pointer;
  }
  .frame {
    width: 100%;
    flex: 1;
    min-height: 480px;
    border: 1px solid var(--line);
    border-radius: var(--radius);
    background: #ffffff;
  }
  .dbody {
    padding: 20px;
    min-width: 400px;
  }
  .dfoot {
    display: flex;
    justify-content: flex-end;
    gap: 8px;
    padding: 12px 20px;
    border-top: 1px solid var(--line);
  }
</style>
