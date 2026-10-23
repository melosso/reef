import { beforeEach, describe, expect, it } from 'vitest';
import { flushSync } from 'svelte';
import { urlPaging } from '../src/lib/paging.svelte';
import { navigate } from '../src/lib/router.svelte';

describe('urlPaging', () => {
  beforeEach(() => navigate('/executions?status=Failed', true));

  it('reads the page and size from the URL with defaults', async () => {
    await navigate('/executions?page=3&size=50', true);
    const paging = urlPaging(25);
    expect(paging.page).toBe(3);
    expect(paging.size).toBe(50);
  });

  it('writes the page and keeps other query values', () => {
    const paging = urlPaging(25);
    paging.page = 2;
    flushSync();
    expect(location.search).toBe('?status=Failed&page=2');
  });

  it('resets the page and drops default values when the size changes', async () => {
    await navigate('/executions?page=4', true);
    const paging = urlPaging(25);
    paging.size = 25;
    flushSync();
    expect(location.search).toBe('');
    paging.size = 100;
    expect(location.search).toBe('?size=100');
  });
});
