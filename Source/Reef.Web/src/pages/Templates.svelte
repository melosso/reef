<script lang="ts">
  import { Copy, Download, FileDown, LibraryBig, Pencil, Plus, Trash2 } from '@lucide/svelte';
  import Page from '../components/Page.svelte';
  import Sheet from '../components/Sheet.svelte';
  import StatusMark from '../components/StatusMark.svelte';
  import CodeEditor from '../editor/CodeEditor.svelte';
  import { api } from '../lib/api';
  import { confirm } from '../lib/confirm.svelte';
  import { copyText, notify } from '../lib/notify';
  import { navigate, route, setQuery } from '../lib/router.svelte';
  import { tooltip } from '../lib/tooltip';
  import type { ExportProfile } from '../models/ExportProfile';
  import type { GitHubFile } from '../models/GitHubFile';
  import type { QueryTemplate } from '../models/QueryTemplate';
  import type { TemplatePreview } from '../models/TemplatePreview';
  import type { TemplateValidation } from '../models/TemplateValidation';

  const TYPES: [string, string][] = [
    ['ScribanTemplate', 'Scriban template'],
    ['DocumentTemplate', 'Document template'],
    ['XsltTransform', 'XSLT transform'],
    ['ForXmlRaw', 'FOR XML RAW'],
    ['ForXmlAuto', 'FOR XML AUTO'],
    ['ForXmlPath', 'FOR XML PATH'],
    ['ForXmlExplicit', 'FOR XML EXPLICIT'],
    ['ForJson', 'FOR JSON AUTO'],
    ['ForJsonPath', 'FOR JSON PATH'],
  ];
  const TYPE_LABEL = Object.fromEntries(TYPES);
  const FORMATS: Record<string, string[]> = {
    ScribanTemplate: ['CSV', 'JSON', 'XML', 'YAML', 'HTML', 'EDI', 'Text'],
    DocumentTemplate: ['PDF', 'DOCX', 'ODT'],
    XsltTransform: ['XML'],
    ForXmlRaw: ['XML'],
    ForXmlAuto: ['XML'],
    ForXmlPath: ['XML'],
    ForXmlExplicit: ['XML'],
    ForJson: ['JSON'],
    ForJsonPath: ['JSON'],
  };
  const VALIDATED = ['ScribanTemplate', 'DocumentTemplate'];
  const EXT_FORMAT: Record<string, string> = {
    xml: 'XML',
    json: 'JSON',
    csv: 'CSV',
    txt: 'Text',
    text: 'Text',
    html: 'HTML',
    htm: 'HTML',
    pdf: 'PDF',
    docx: 'DOCX',
    odt: 'ODT',
    yaml: 'YAML',
    yml: 'YAML',
    md: 'Text',
  };

  interface Doc {
    pageSize: string;
    orientation: string;
    pageNumbers: boolean;
    watermark: string;
  }
  interface Form {
    id?: number;
    name: string;
    type: string;
    description: string;
    template: string;
    outputFormat: string;
    tags: string;
    isActive: boolean;
    doc: Doc;
  }
  interface GitTemplate {
    file: string;
    name: string;
    type: string;
    outputFormat: string;
    url: string;
  }

  let templates = $state<QueryTemplate[]>([]);
  let loaded = $state(false);
  let search = $state('');
  let typeFilter = $state('');
  let form = $state<Form | null>(null);
  let validated = $state<'pending' | 'valid' | 'invalid' | null>(null);
  let preview = $state<string | null>(null);
  let busy = $state<string | null>(null);
  let gallery = $state<GitTemplate[] | null>(null);
  let gallerySearch = $state('');
  let guidance = $state<{ name: string; snippets: [string, string][] } | null>(null);
  let pendingGuide = $state<string>('');

  async function load() {
    templates = await api.get<QueryTemplate[]>('/api/templates').catch((e) => {
      notify.fail(e, 'templates');
      return [];
    });
    loaded = true;
  }

  $effect(() => {
    load();
  });

  $effect(() => {
    const id = route.query.get('id');
    if (id && loaded && form?.id !== Number(id)) edit(Number(id));
  });

  const visible = $derived(
    templates.filter((t) => {
      const term = search.trim().toLowerCase();
      return (
        (!typeFilter || t.type === typeFilter) &&
        (!term ||
          [t.name, t.description, t.tags, t.outputFormat].some((v) =>
            String(v ?? '')
              .toLowerCase()
              .includes(term),
          ))
      );
    }),
  );

  function parseDirectives(content: string): Doc {
    const doc: Doc = { pageSize: 'A4', orientation: 'Portrait', pageNumbers: true, watermark: '' };
    for (const m of content.matchAll(/\{\{!\s*(\w+):\s*([^}]+)\}\}/g)) {
      const key = m[1].toLowerCase();
      const value = m[2].trim();
      if (key === 'pagesize') doc.pageSize = value;
      if (key === 'orientation') doc.orientation = value;
      if (key === 'includepagenumbers') doc.pageNumbers = value.toLowerCase() !== 'false';
      if (key === 'watermark') doc.watermark = value;
    }
    return doc;
  }

  function stripDirectives(content: string) {
    return content.replace(/\{\{!\s*\w+:.*?\}\}\n*/g, '');
  }

  function blank(type = 'ScribanTemplate'): Form {
    return {
      name: '',
      type,
      description: '',
      template: '',
      outputFormat: FORMATS[type][0],
      tags: '',
      isActive: true,
      doc: { pageSize: 'A4', orientation: 'Portrait', pageNumbers: true, watermark: '' },
    };
  }

  function create() {
    form = blank();
    validated = 'pending';
    preview = null;
  }

  async function edit(id: number) {
    try {
      const t = await api.get<QueryTemplate>(`/api/templates/${id}`);
      form = {
        id: t.id,
        name: t.name,
        type: t.type,
        description: t.description ?? '',
        outputFormat: t.outputFormat,
        tags: t.tags ?? '',
        isActive: t.isActive,
        template: t.type === 'DocumentTemplate' ? stripDirectives(t.template ?? '').trim() : (t.template ?? ''),
        doc: parseDirectives(t.template ?? ''),
      };
      validated = VALIDATED.includes(t.type) ? 'pending' : null;
      preview = null;
    } catch (e) {
      notify.fail(e);
    }
  }

  function close() {
    form = null;
    if (route.query.get('id')) setQuery('id', null);
  }

  function changeType(type: string) {
    if (!form) return;
    form.type = type;
    if (!FORMATS[type].includes(form.outputFormat)) form.outputFormat = FORMATS[type][0];
    validated = VALIDATED.includes(type) ? 'pending' : null;
  }

  function content(f: Form): string {
    if (f.type !== 'DocumentTemplate') return f.template;
    let d = `{{! format: ${f.outputFormat.toLowerCase()} }}\n{{! pageSize: ${f.doc.pageSize} }}\n{{! orientation: ${f.doc.orientation} }}\n`;
    if (!f.doc.pageNumbers) d += '{{! includePageNumbers: false }}\n';
    if (f.doc.watermark.trim()) d += `{{! watermark: ${f.doc.watermark.trim()} }}\n`;
    return `${d}\n${stripDirectives(f.template).trim()}`;
  }

  async function validate(): Promise<boolean> {
    if (!form || !VALIDATED.includes(form.type)) return true;
    busy = 'validate';
    try {
      const result = await api.post<TemplateValidation>('/api/templates/validate', { template: content(form), type: form.type });
      validated = result?.isValid ? 'valid' : 'invalid';
      if (result?.isValid) notify.success(result.message || 'Template is valid', { key: 'tpl-validate' });
      else notify.error(`Template is invalid. ${result?.errorMessage ?? ''}`.trim(), { key: 'tpl-validate' });
      return !!result?.isValid;
    } catch (e) {
      validated = 'invalid';
      notify.fail(e, 'tpl-validate');
      return false;
    } finally {
      busy = null;
    }
  }

  async function runPreview() {
    if (!form || !(await validate())) return;
    busy = 'preview';
    try {
      const result = await api.post<TemplatePreview>('/api/templates/preview', {
        template: content(form),
        type: form.type,
        outputFormat: form.outputFormat,
      });
      preview = result?.preview || result?.output || 'No preview available';
    } catch (e) {
      notify.fail(e, 'tpl-preview');
    } finally {
      busy = null;
    }
  }

  async function save(e: SubmitEvent) {
    e.preventDefault();
    if (!form || !(await validate())) return;
    busy = 'save';
    try {
      const body = {
        ...(form.id ? { id: form.id } : {}),
        name: form.name,
        description: form.description,
        type: form.type,
        template: content(form),
        outputFormat: form.outputFormat,
        tags: form.tags,
        isActive: form.isActive,
      };
      if (form.id) await api.put(`/api/templates/${form.id}`, body);
      else await api.post('/api/templates', body);
      notify.success(`${form.name} saved`, { key: 'tpl-save' });
      const name = form.name;
      close();
      await load();
      if (pendingGuide) showGuidance(name, pendingGuide);
      pendingGuide = '';
    } catch (err) {
      notify.fail(err, 'tpl-save');
    } finally {
      busy = null;
    }
  }

  async function remove(t: QueryTemplate) {
    const users = await api.get<ExportProfile[]>(`/api/profiles?templateId=${t.id}`).catch(() => []);
    if (users?.length) {
      notify.warning(
        `${t.name} is used by ${users.length} ${users.length === 1 ? 'profile' : 'profiles'}: ${users.map((p) => p.name).join(', ')}. Turn the template off instead.`,
      );
      return;
    }
    if (
      !(await confirm({
        title: `Delete ${t.name}?`,
        message: 'Deleted templates cannot be restored. Turning the template off keeps it for later use.',
        confirm: 'Delete template',
        danger: true,
      }))
    )
      return;
    try {
      await api.del(`/api/templates/${t.id}`);
      notify.success(`${t.name} deleted`);
      if (form?.id === t.id) close();
      await load();
    } catch (e) {
      notify.fail(e);
    }
  }

  async function toggleActive(t: QueryTemplate) {
    try {
      const full = await api.get<QueryTemplate>(`/api/templates/${t.id}`);
      await api.put(`/api/templates/${t.id}`, { ...full, isActive: !full.isActive });
      notify.success(`${t.name} turned ${full.isActive ? 'off' : 'on'}`);
      await load();
    } catch (e) {
      notify.fail(e);
    }
  }

  function toml(t: QueryTemplate): string {
    const esc = (s: string | null | undefined) =>
      !s ? '""' : s.includes('\n') ? `"""\n${s}\n"""` : `"${s.replace(/\\/g, '\\\\').replace(/"/g, '\\"')}"`;
    const tags = t.tags
      ? t.tags
          .split(',')
          .map((x: string) => `"${x.trim()}"`)
          .join(', ')
      : '';
    return [
      '# Reef query template export',
      `# Exported: ${new Date().toISOString()}`,
      '',
      '[template]',
      `name = ${esc(t.name)}`,
      `description = ${esc(t.description)}`,
      `type = "${t.type}"`,
      `output_format = "${t.outputFormat}"`,
      `is_active = ${t.isActive}`,
      `tags = [${tags}]`,
      '',
      '# Template content',
      `content = ${esc(t.template)}`,
    ].join('\n');
  }

  async function exportToml(t: QueryTemplate) {
    try {
      const full = await api.get<QueryTemplate>(`/api/templates/${t.id}`);
      const a = document.createElement('a');
      a.href = URL.createObjectURL(new Blob([toml(full)], { type: 'text/plain' }));
      a.download = `${full.name.replace(/[^a-z0-9_-]/gi, '_').toLowerCase()}.toml`;
      a.click();
      URL.revokeObjectURL(a.href);
    } catch (e) {
      notify.fail(e);
    }
  }

  function parseFile(file: string, url: string): GitTemplate | null {
    const ext = /\.([^.]+)$/.exec(file)?.[1];
    if (!ext) return null;
    const type = TYPES.map(([t]) => t).find((t) => file.includes(t));
    if (!type) return null;
    const name = file.substring(0, file.indexOf(type)).replace(/[._-]+$/, '');
    if (!name) return null;
    return { file, name: name.replace(/_/g, ' '), type, outputFormat: EXT_FORMAT[ext.toLowerCase()] ?? 'Text', url };
  }

  async function openGallery() {
    gallery = [];
    gallerySearch = '';
    try {
      const response = await fetch('https://api.github.com/repos/melosso/reef/contents/Templates');
      if (!response.ok) throw new Error(`GitHub returned status ${response.status}.`);
      const files: GitHubFile[] = await response.json();
      gallery = files
        .filter((f) => f.type === 'file' && !f.name.toLowerCase().endsWith('.md'))
        .map((f) => parseFile(f.name, f.download_url))
        .filter((t): t is GitTemplate => t !== null);
    } catch (e) {
      gallery = null;
      notify.error(`Template gallery failed to load. ${(e as Error).message}`);
    }
  }

  async function pick(t: GitTemplate) {
    try {
      const text = await fetch(t.url).then((r) => {
        if (!r.ok) throw new Error('Template download failed.');
        return r.text();
      });
      const md = await fetch(t.url.replace(/\.[^.]+$/, '.md'))
        .then((r) => (r.ok ? r.text() : ''))
        .catch(() => '');
      const first = md.split('\n\n')[0] ?? '';
      const f = blank(t.type);
      f.name = t.name;
      f.description = first.length > 200 ? `${first.substring(0, 200)}...` : first;
      f.template = t.type === 'DocumentTemplate' ? stripDirectives(text).trim() : text;
      f.doc = parseDirectives(text);
      f.outputFormat = FORMATS[t.type].includes(t.outputFormat) ? t.outputFormat : FORMATS[t.type][0];
      f.tags = 'github, imported';
      gallery = null;
      form = f;
      validated = VALIDATED.includes(t.type) ? 'pending' : null;
      pendingGuide = /```(sql|mysql|postgresql)/i.test(md) ? md : '';
    } catch (e) {
      notify.fail(e);
    }
  }

  function showGuidance(name: string, md: string) {
    const snippets: [string, string][] = [];
    for (const [label, lang] of [
      ['SQL Server', 'sql'],
      ['MySQL', 'mysql'],
      ['PostgreSQL', 'postgresql'],
    ]) {
      const m = new RegExp('```' + lang + '([\\s\\S]*?)```', 'i').exec(md);
      if (m) snippets.push([label, m[1].trim()]);
    }
    if (snippets.length) guidance = { name, snippets };
  }

  const galleryItems = $derived(
    (gallery ?? []).filter(
      (t) => !gallerySearch.trim() || `${t.name} ${t.type} ${t.outputFormat}`.toLowerCase().includes(gallerySearch.trim().toLowerCase()),
    ),
  );
</script>

<Page crumbs={[{ label: 'Templates' }]}>
  {#snippet actions()}
    <button type="button" class="btn" onclick={openGallery}><LibraryBig size={15} />Template gallery</button>
    <button type="button" class="btn primary" onclick={create}><Plus size={15} />New template</button>
  {/snippet}
  <div class="page-body">
    <div class="toolbar">
      <input type="search" placeholder="Search name, description, tag or format" aria-label="Search templates" bind:value={search} />
      <select aria-label="Type" bind:value={typeFilter}>
        <option value="">All types</option>
        {#each TYPES as [value, label] (value)}<option {value}>{label}</option>{/each}
      </select>
      <span class="spacer"></span>
      <span class="muted"><span class="num">{visible.length}</span> of <span class="num">{templates.length}</span></span>
    </div>
    <div class="table-wrap">
      <table class="data">
        <thead><tr><th>Name</th><th>Type</th><th>Format</th><th>Tags</th><th>Status</th><th><span class="sr">Actions</span></th></tr></thead
        >
        <tbody>
          {#each visible as t (t.id)}
            <tr
              class="clickable"
              onclick={(e) => {
                if (!(e.target as Element).closest('button')) setQuery('id', String(t.id));
              }}
            >
              <td
                ><span class="strong">{t.name}</span>{#if t.description}<span class="sub">{t.description}</span>{/if}</td
              >
              <td>{TYPE_LABEL[t.type] ?? t.type}</td>
              <td>{t.outputFormat}</td>
              <td
                >{(t.tags ?? '')
                  .split(',')
                  .map((x: string) => x.trim())
                  .filter(Boolean)
                  .join(', ')}</td
              >
              <td
                ><button type="button" class="plain" use:tooltip={t.isActive ? 'Turn off' : 'Turn on'} onclick={() => toggleActive(t)}
                  ><StatusMark kind={t.isActive ? 'ok' : 'off'} label={t.isActive ? 'On' : 'Off'} /></button
                ></td
              >
              <td class="actions">
                <button
                  type="button"
                  class="btn ghost icon"
                  aria-label="Edit {t.name}"
                  use:tooltip={'Edit'}
                  onclick={() => setQuery('id', String(t.id))}><Pencil size={15} /></button
                >
                <button
                  type="button"
                  class="btn ghost icon"
                  aria-label="Export {t.name} as TOML"
                  use:tooltip={'Export TOML'}
                  onclick={() => exportToml(t)}><FileDown size={15} /></button
                >
                <button
                  type="button"
                  class="btn ghost icon danger"
                  aria-label="Delete {t.name}"
                  use:tooltip={'Delete'}
                  onclick={() => remove(t)}><Trash2 size={15} /></button
                >
              </td>
            </tr>
          {:else}
            <tr
              ><td colspan="6" class="empty">
                {#if !loaded}Loading templates
                {:else if templates.length === 0}
                  <div class="empty-state">
                    <h2>No templates</h2>
                    <p>A template shapes query rows into CSV, XML, JSON, EDI or a PDF document.</p>
                    <div class="toolbar">
                      <button type="button" class="btn primary" onclick={create}>New template</button><button
                        type="button"
                        class="btn"
                        onclick={openGallery}>Template gallery</button
                      >
                    </div>
                  </div>
                {:else}No template matches the search.{/if}
              </td></tr
            >
          {/each}
        </tbody>
      </table>
    </div>
  </div>
</Page>

{#if form}
  <Sheet title={form.id ? form.name || 'Template' : 'New template'} onclose={close} wide>
    <form id="template-form" class="fields" onsubmit={save}>
      <div class="grid2">
        <div class="field"><label for="t-name">Name</label><input id="t-name" bind:value={form.name} autocomplete="off" /></div>
        <div class="field">
          <label for="t-type">Type</label>
          <select id="t-type" value={form.type} onchange={(e) => changeType(e.currentTarget.value)}
            >{#each TYPES as [value, label] (value)}<option {value}>{label}</option>{/each}</select
          >
        </div>
      </div>
      <div class="field"><label for="t-desc">Description</label><input id="t-desc" bind:value={form.description} /></div>
      <div class="field">
        <label
          for="t-content"
          use:tooltip={form.type === 'ScribanTemplate' || form.type === 'DocumentTemplate'
            ? 'Insert row values with double braces around the column name'
            : null}>Template</label
        >
        <CodeEditor
          id="t-content"
          value={form.template}
          rows={14}
          invalid={validated === 'invalid'}
          onchange={(v) => {
            form!.template = v;
            if (validated) validated = 'pending';
          }}
        />
        {#if validated}
          <div class="vrow">
            <StatusMark
              kind={validated === 'valid' ? 'ok' : validated === 'invalid' ? 'err' : 'idle'}
              label={validated === 'valid' ? 'Valid' : validated === 'invalid' ? 'Invalid' : 'Not validated'}
            />
            <button type="button" class="btn" aria-busy={busy === 'validate'} onclick={validate}>Validate</button>
            <button type="button" class="btn" aria-busy={busy === 'preview'} onclick={runPreview}>Preview</button>
          </div>
        {/if}
      </div>
      {#if form.type === 'DocumentTemplate'}
        <div class="grid2">
          <div class="field">
            <label for="t-size">Page size</label><select id="t-size" bind:value={form.doc.pageSize}
              ><option value="A4">A4</option><option value="Letter">Letter</option><option value="Legal">Legal</option></select
            >
          </div>
          <div class="field">
            <label for="t-orient">Orientation</label><select id="t-orient" bind:value={form.doc.orientation}
              ><option>Portrait</option><option>Landscape</option></select
            >
          </div>
        </div>
        <label class="check"><input type="checkbox" bind:checked={form.doc.pageNumbers} />Page numbers</label>
        <div class="field">
          <label for="t-mark">Watermark</label><input id="t-mark" placeholder="DRAFT" bind:value={form.doc.watermark} />
        </div>
      {/if}
      <div class="grid2">
        <div class="field">
          <label for="t-format">Output format</label><select id="t-format" bind:value={form.outputFormat}
            >{#each FORMATS[form.type] as f (f)}<option>{f}</option>{/each}</select
          >
        </div>
        <div class="field">
          <label for="t-tags">Tags</label><input
            id="t-tags"
            bind:value={form.tags}
            placeholder="xml, finance"
            use:tooltip={'Comma-separated'}
          />
        </div>
      </div>
      <label class="check"><input type="checkbox" role="switch" bind:checked={form.isActive} />Active</label>
      {#if preview !== null}
        <section class="fields">
          <h3 class="row">
            Preview <span class="grow"></span>
            <button type="button" class="btn ghost icon" aria-label="Copy preview" onclick={() => copyText(preview!)}
              ><Copy size={14} /></button
            >
            <button
              type="button"
              class="btn ghost icon"
              aria-label="Download preview"
              onclick={() => {
                const a = document.createElement('a');
                a.href = URL.createObjectURL(new Blob([preview!]));
                a.download = `preview.${form!.outputFormat.toLowerCase()}`;
                a.click();
              }}><Download size={14} /></button
            >
          </h3>
          <pre class="code">{preview}</pre>
        </section>
      {/if}
    </form>
    {#snippet footer()}
      <button type="button" class="btn" onclick={close}>Cancel</button>
      <button type="submit" form="template-form" class="btn primary" aria-busy={busy === 'save'}>Save</button>
    {/snippet}
  </Sheet>
{/if}

{#if gallery}
  <Sheet title="Template gallery" onclose={() => (gallery = null)} wide>
    <p class="muted">Templates from the Reef repository on GitHub. A selected template opens in the editor before it is saved.</p>
    <input type="search" placeholder="Search templates" aria-label="Search the gallery" bind:value={gallerySearch} />
    <div class="table-wrap">
      <table class="data">
        <thead><tr><th>Name</th><th>Type</th><th>Format</th></tr></thead>
        <tbody>
          {#each galleryItems as t (t.file)}
            <tr class="clickable" onclick={() => pick(t)}
              ><td class="strong">{t.name}</td><td>{TYPE_LABEL[t.type]}</td><td>{t.outputFormat}</td></tr
            >
          {:else}
            <tr
              ><td colspan="3" class="empty">{gallery.length ? 'No template matches the search.' : 'Loading templates from GitHub'}</td></tr
            >
          {/each}
        </tbody>
      </table>
    </div>
  </Sheet>
{/if}

{#if guidance}
  <Sheet title="Query for {guidance.name}" onclose={() => (guidance = null)} wide>
    <p class="muted">The template expects the columns from this query. Paste the query into the source step of an export profile.</p>
    {#each guidance.snippets as [db, sql], i (i)}
      <section class="fields">
        <h3 class="row">
          {db}<span class="grow"></span><button
            type="button"
            class="btn ghost icon"
            aria-label="Copy {db} query"
            onclick={() => copyText(sql)}><Copy size={14} /></button
          >
        </h3>
        <pre class="code">{sql}</pre>
      </section>
    {/each}
    {#snippet footer()}
      <button type="button" class="btn" onclick={() => (guidance = null)}>Close</button>
      <button
        type="button"
        class="btn primary"
        onclick={() => {
          guidance = null;
          navigate('/profiles/export/new');
        }}>New export profile</button
      >
    {/snippet}
  </Sheet>
{/if}

<style>
  .strong {
    font-weight: 500;
    display: block;
  }
  .sub {
    display: block;
    font-size: 12px;
    color: var(--muted);
    max-width: 420px;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }
  .vrow {
    display: flex;
    align-items: center;
    gap: 8px;
    margin-top: 8px;
  }
  .row {
    display: flex;
    align-items: center;
    gap: 6px;
  }
  .grow {
    flex: 1;
  }
  .plain {
    border: 0;
    background: none;
    padding: 0;
    cursor: pointer;
    font: inherit;
    color: inherit;
  }
</style>
