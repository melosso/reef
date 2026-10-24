import { fireEvent, render, screen } from '@testing-library/svelte';
import { createRawSnippet } from 'svelte';
import { describe, expect, it, vi } from 'vitest';
import Page from '../src/components/Page.svelte';

vi.hoisted(() => {
  window.matchMedia ??= () => ({ matches: false, addEventListener: () => {}, removeEventListener: () => {} }) as unknown as MediaQueryList;
});

const children = createRawSnippet(() => ({ render: () => '<div></div>' }));

function renderPage(onrename: (name: string) => void) {
  render(Page, {
    props: {
      crumbs: [
        { label: 'Profiles', href: '/profiles' },
        { label: 'Orders', onrename },
      ],
      children,
    },
  });
}

describe('breadcrumb rename', () => {
  it('renames on double click and enter', async () => {
    const onrename = vi.fn();
    renderPage(onrename);

    await fireEvent.dblClick(screen.getByText('Orders'));
    const input = screen.getByLabelText<HTMLInputElement>('Name');
    await fireEvent.input(input, { target: { value: 'Invoices' } });
    await fireEvent.keyDown(input, { key: 'Enter' });

    expect(onrename).toHaveBeenCalledExactlyOnceWith('Invoices');
    expect(screen.queryByLabelText('Name')).toBeNull();
  });

  it('keeps the name on escape', async () => {
    const onrename = vi.fn();
    renderPage(onrename);

    await fireEvent.dblClick(screen.getByText('Orders'));
    const input = screen.getByLabelText<HTMLInputElement>('Name');
    await fireEvent.input(input, { target: { value: 'Invoices' } });
    await fireEvent.keyDown(input, { key: 'Escape' });

    expect(onrename).not.toHaveBeenCalled();
  });
});
