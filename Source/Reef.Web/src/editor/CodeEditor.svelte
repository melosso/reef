<script lang="ts">
  import { onMount } from 'svelte';
  import { EditorView, minimalSetup } from 'codemirror';
  import { lineNumbers, placeholder as hint } from '@codemirror/view';
  import { sql } from '@codemirror/lang-sql';
  import { HighlightStyle, syntaxHighlighting } from '@codemirror/language';
  import { tags } from '@lezer/highlight';

  let {
    id,
    value,
    language = 'plain',
    placeholder = '',
    invalid = false,
    rows = 8,
    onchange,
  }: {
    id: string;
    value: string;
    language?: 'sql' | 'plain';
    placeholder?: string;
    invalid?: boolean;
    rows?: number;
    onchange: (value: string) => void;
  } = $props();

  let host: HTMLDivElement;
  let view: EditorView | undefined;

  const theme = EditorView.theme({
    '&': { fontSize: '13px', backgroundColor: 'var(--surface)', color: 'var(--ink)' },
    '.cm-content': { fontFamily: 'var(--font)', caretColor: 'var(--ink)', padding: '6px 0' },
    '.cm-gutters': { backgroundColor: 'var(--bg)', color: 'var(--muted)', border: 'none', borderRight: '1px solid var(--line)' },
    '.cm-activeLine': { backgroundColor: 'transparent' },
    '&.cm-focused': { outline: 'none' },
    '.cm-selectionBackground, &.cm-focused .cm-selectionBackground, ::selection': { backgroundColor: 'var(--accent-soft) !important' },
    '.cm-placeholder': { color: 'var(--muted)' },
  });

  const highlight = HighlightStyle.define([
    { tag: [tags.keyword, tags.operatorKeyword], color: 'var(--accent-ink)', fontWeight: '600' },
    { tag: [tags.string, tags.special(tags.string)], color: 'var(--warn)' },
    { tag: [tags.number, tags.bool, tags.null], color: 'var(--ink)', fontFamily: 'var(--mono)' },
    { tag: [tags.comment, tags.lineComment, tags.blockComment], color: 'var(--muted)', fontStyle: 'italic' },
    { tag: [tags.typeName, tags.standard(tags.name)], color: 'var(--soft)' },
  ]);

  onMount(() => {
    view = new EditorView({
      parent: host,
      doc: value,
      extensions: [
        minimalSetup,
        lineNumbers(),
        EditorView.lineWrapping,
        theme,
        syntaxHighlighting(highlight),
        hint(placeholder),
        EditorView.contentAttributes.of({ id, 'aria-multiline': 'true', spellcheck: 'false' }),
        ...(language === 'sql' ? [sql()] : []),
        EditorView.updateListener.of((update) => {
          if (update.docChanged) onchange(update.state.doc.toString());
        }),
      ],
    });
    return () => view?.destroy();
  });

  $effect(() => {
    if (view && value !== view.state.doc.toString()) {
      view.dispatch({ changes: { from: 0, to: view.state.doc.length, insert: value } });
    }
  });
</script>

<div class="code" class:invalid bind:this={host} style:min-height="{rows * 20 + 14}px"></div>

<style>
  .code {
    border: 1px solid var(--line-strong);
    border-radius: var(--radius);
    overflow: hidden;
    resize: vertical;
    max-height: 640px;
  }
  .code.invalid {
    border-color: var(--err);
  }
  .code:focus-within {
    outline: 2px solid var(--accent);
    outline-offset: 1px;
  }
  .code :global(.cm-editor) {
    height: 100%;
  }
</style>
