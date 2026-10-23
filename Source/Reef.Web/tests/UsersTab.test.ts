import { render, screen } from '@testing-library/svelte';
import { afterEach, describe, expect, it, vi } from 'vitest';
import UsersTab from '../src/components/admin/UsersTab.svelte';
import { session } from '../src/lib/session.svelte';

describe('users table', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('separates username, display name and the current user marker', async () => {
    session.username = 'admin@reef.local';
    const users = [
      {
        id: 1,
        username: 'admin@reef.local',
        displayName: 'Administrator',
        role: 'Admin',
        isActive: true,
        createdAt: '2026-01-01T00:00:00Z',
      },
    ];
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => new Response(JSON.stringify(users), { headers: { 'content-type': 'application/json' } })),
    );
    render(UsersTab);
    const cell = await screen.findByText('admin@reef.local');
    expect(cell.closest('td')?.textContent?.replace(/\s+/g, ' ').trim()).toBe('admin@reef.local Administrator (you)');
  });
});
