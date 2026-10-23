<script lang="ts">
  import { Info, Plus, X } from '@lucide/svelte';
  import { tooltip } from '../lib/tooltip';
  import CodeEditor from './CodeEditor.svelte';
  import { defaultsFor, getPath, setPath, textOf } from './config';
  import type { CatalogField, Config, FieldOption, Options } from './types';

  let {
    field,
    config,
    options,
    error,
    onchange,
  }: {
    field: CatalogField;
    config: Config;
    options: Options;
    error?: string;
    onchange: (structural: boolean) => void;
  } = $props();

  const id = $derived(`f-${field.key.replace(/\W/g, '-')}`);
  const value = $derived(getPath(config, field.key) ?? field.default);
  const choices = $derived<FieldOption[]>(field.optionsSource ? (options[field.optionsSource] ?? []) : (field.options ?? []));
  const hasEmpty = $derived(choices.some((o) => o.value === ''));

  function commit(next: unknown, structural = false) {
    setPath(config, field.key, next);
    onchange(structural);
  }

  function emptyLabel(): string {
    if (!field.required) return 'None';
    return choices.length === 0 ? 'Nothing to choose yet' : 'Choose';
  }

  function jsonText(v: unknown): string {
    if (v === undefined || v === null) return '';
    return typeof v === 'string' ? v : JSON.stringify(v, null, 2);
  }

  function rows(): Config[] {
    return Array.isArray(value) ? value : [];
  }

  function updateRows(next: Config[], structural = false) {
    commit(next.length ? next : undefined, structural);
  }
</script>

{#if field.kind === 'bool'}
  <label class="check" for={id} use:tooltip={error ? { text: error, placement: 'top-start' } : null}>
    <input
      {id}
      type="checkbox"
      checked={value === true}
      aria-invalid={error ? 'true' : undefined}
      onchange={(e) => commit(e.currentTarget.checked, true)}
    />
    <span class:invalid={!!error}>{field.label}</span>
    {#if field.tooltip}<span class="hint" use:tooltip={{ text: field.tooltip, placement: 'left' }}><Info size={13} /></span>{/if}
  </label>
{:else}
  <div class="field">
    <label for={id} class:invalid={!!error} use:tooltip={error ? { text: error, placement: 'top-start' } : null}>
      {field.label}{#if field.required}<span class="req" aria-hidden="true">*</span>{/if}
      {#if field.tooltip}<span class="hint" use:tooltip={{ text: field.tooltip, placement: 'left' }}><Info size={13} /></span>{/if}
    </label>
    {#if field.kind === 'select'}
      <select
        {id}
        aria-invalid={error ? 'true' : undefined}
        value={textOf(value)}
        onchange={(e) => {
          const raw = e.currentTarget.value;
          commit(raw === '' && !hasEmpty ? undefined : field.optionsSource ? Number(raw) : raw, true);
        }}
      >
        {#if !hasEmpty}<option value="">{emptyLabel()}</option>{/if}
        {#each choices as option (option.value)}<option value={textOf(option.value)}>{option.label}</option>{/each}
      </select>
    {:else if field.kind === 'code'}
      <CodeEditor
        {id}
        value={textOf(value)}
        language={/query|sql/i.test(field.key) ? 'sql' : 'plain'}
        placeholder={field.placeholder}
        invalid={!!error}
        onchange={(v) => commit(v || undefined)}
      />
    {:else if field.kind === 'textarea'}
      <textarea
        {id}
        rows="3"
        placeholder={field.placeholder}
        aria-invalid={error ? 'true' : undefined}
        spellcheck="false"
        value={textOf(value)}
        oninput={(e) => commit(e.currentTarget.value || undefined)}></textarea>
    {:else if field.kind === 'json'}
      <textarea
        {id}
        rows="4"
        placeholder={field.placeholder || '{ }'}
        aria-invalid={error ? 'true' : undefined}
        spellcheck="false"
        value={jsonText(value)}
        oninput={(e) => {
          const text = e.currentTarget.value.trim();
          if (!text) return commit(undefined);
          try {
            commit(JSON.parse(text));
          } catch {
            commit(text);
          }
        }}></textarea>
    {:else if field.kind === 'number'}
      <input
        {id}
        type="number"
        inputmode="numeric"
        placeholder={field.placeholder}
        aria-invalid={error ? 'true' : undefined}
        value={textOf(value)}
        oninput={(e) => commit(e.currentTarget.value === '' ? undefined : Number(e.currentTarget.value))}
      />
    {:else if field.kind === 'list'}
      <input
        {id}
        type="text"
        placeholder={field.placeholder}
        aria-invalid={error ? 'true' : undefined}
        value={Array.isArray(value) ? value.join(', ') : textOf(value)}
        oninput={(e) => {
          const items = e.currentTarget.value
            .split(',')
            .map((s) => s.trim())
            .filter(Boolean);
          commit(items.length ? items : undefined);
        }}
      />
    {:else if field.kind === 'table'}
      <div class="table" {id} aria-invalid={error ? 'true' : undefined}>
        <table>
          <thead>
            <tr>
              {#each field.columns ?? [] as column (column.key)}<th class:bool={column.kind === 'bool'} use:tooltip={column.tooltip}
                  >{column.label}</th
                >{/each}
              <th class="act"><span class="sr">Actions</span></th>
            </tr>
          </thead>
          <tbody>
            {#each rows() as row, index (index)}
              <tr>
                {#each field.columns ?? [] as column (column.key)}
                  <td class:bool={column.kind === 'bool'}>
                    {#if column.kind === 'bool'}
                      <input
                        type="checkbox"
                        aria-label={column.label}
                        checked={row[column.key] === true}
                        onchange={(e) => {
                          row[column.key] = e.currentTarget.checked;
                          updateRows(rows());
                        }}
                      />
                    {:else if column.kind === 'select'}
                      {@const colChoices = column.optionsSource ? (options[column.optionsSource] ?? []) : (column.options ?? [])}
                      <select
                        aria-label={column.label}
                        value={textOf(row[column.key])}
                        onchange={(e) => {
                          const raw = e.currentTarget.value;
                          if (raw) row[column.key] = column.optionsSource ? Number(raw) : raw;
                          else delete row[column.key];
                          updateRows(rows());
                        }}
                      >
                        {#if !colChoices.some((o) => o.value === '')}<option value="">Choose</option>{/if}
                        {#each colChoices as option (option.value)}<option value={textOf(option.value)}>{option.label}</option>{/each}
                      </select>
                    {:else}
                      <input
                        type="text"
                        aria-label={column.label}
                        value={textOf(row[column.key])}
                        oninput={(e) => {
                          row[column.key] = e.currentTarget.value;
                          updateRows(rows());
                        }}
                      />
                    {/if}
                  </td>
                {/each}
                <td class="act"
                  ><button
                    type="button"
                    class="btn ghost icon"
                    aria-label="Remove row"
                    onclick={() => {
                      const next = rows().slice();
                      next.splice(index, 1);
                      updateRows(next, true);
                    }}><X size={14} /></button
                  ></td
                >
              </tr>
            {/each}
          </tbody>
        </table>
        <button type="button" class="btn ghost add" onclick={() => updateRows([...rows(), defaultsFor(field.columns ?? [])], true)}
          ><Plus size={14} />Add row</button
        >
      </div>
    {:else}
      <input
        {id}
        type={field.kind === 'password' ? 'password' : field.format === 'email' ? 'email' : 'text'}
        autocomplete={field.kind === 'password' ? 'new-password' : 'off'}
        placeholder={field.placeholder}
        aria-invalid={error ? 'true' : undefined}
        value={textOf(value)}
        oninput={(e) => commit(e.currentTarget.value === '' ? undefined : e.currentTarget.value)}
      />
    {/if}
  </div>
{/if}

<style>
  label {
    display: flex;
    align-items: center;
    gap: 4px;
  }
  .req {
    color: var(--err);
  }
  .hint {
    color: var(--muted);
    display: inline-flex;
    cursor: help;
  }
  .invalid {
    color: var(--err);
  }
  .table {
    border: 1px solid var(--line-strong);
    border-radius: var(--radius);
    overflow-x: auto;
  }
  .table[aria-invalid='true'] {
    border-color: var(--err);
  }
  table {
    width: 100%;
    border-collapse: collapse;
  }
  th {
    font-size: 12px;
    font-weight: 500;
    color: var(--muted);
    text-align: left;
    padding: 6px 8px;
    border-bottom: 1px solid var(--line);
    white-space: nowrap;
  }
  td {
    padding: 4px 6px;
    border-bottom: 1px solid var(--line);
  }
  td input:not([type='checkbox']),
  td select {
    padding: 5px 7px !important;
  }
  .bool {
    width: 1%;
    text-align: center;
  }
  .act {
    width: 1%;
  }
  .add {
    margin: 4px;
  }
</style>
