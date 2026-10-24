import { fireEvent, render, screen } from '@testing-library/svelte';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { flushSync } from 'svelte';
import ConfirmDialog from '../src/components/ConfirmDialog.svelte';
import { confirm, settle } from '../src/lib/confirm.svelte';

describe('typed confirmation', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    HTMLDialogElement.prototype.showModal ??= function (this: HTMLDialogElement) {
      this.open = true;
    };
    HTMLDialogElement.prototype.close ??= function (this: HTMLDialogElement) {
      this.open = false;
    };
  });

  afterEach(() => {
    settle(false);
    vi.useRealTimers();
  });

  function open() {
    render(ConfirmDialog);
    const result = confirm({ title: 'Reset the database?', confirm: 'Reset database', danger: true, typed: 'elephant_jungle_stranger' });
    flushSync();
    return result;
  }

  it('blocks pasting the confirmation text', async () => {
    open();
    const input = screen.getByLabelText(/to confirm/);
    const paste = new Event('paste', { bubbles: true, cancelable: true });
    input.dispatchEvent(paste);
    expect(paste.defaultPrevented).toBe(true);
    const drop = new Event('drop', { bubbles: true, cancelable: true });
    input.dispatchEvent(drop);
    expect(drop.defaultPrevented).toBe(true);
  });

  it('keeps the confirm button disabled for 5 seconds after opening', async () => {
    open();
    await fireEvent.input(screen.getByLabelText(/to confirm/), { target: { value: 'elephant_jungle_stranger' } });
    const button = screen.getByRole('button', { name: /Reset database/ });
    expect(button).toHaveProperty('disabled', true);
    vi.advanceTimersByTime(4900);
    flushSync();
    expect(button).toHaveProperty('disabled', true);
    vi.advanceTimersByTime(200);
    flushSync();
    expect(button).toHaveProperty('disabled', false);
  });

  it('does not delay confirmations without typed text', () => {
    render(ConfirmDialog);
    confirm({ title: 'Delete job?', confirm: 'Delete job' });
    flushSync();
    expect(screen.getByRole('button', { name: 'Delete job' })).toHaveProperty('disabled', false);
  });
});
