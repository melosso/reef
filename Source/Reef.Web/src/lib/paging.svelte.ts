import { route, setQuery } from './router.svelte';

// Page and page size live in the URL. Default values stay out of the query.
export function urlPaging(defaultSize: number) {
  return {
    get page(): number {
      return Math.max(1, Number.parseInt(route.query.get('page') ?? '', 10) || 1);
    },
    set page(value: number) {
      setQuery('page', value > 1 ? String(value) : null);
    },
    get size(): number {
      return Number.parseInt(route.query.get('size') ?? '', 10) || defaultSize;
    },
    set size(value: number) {
      setQuery('page', null);
      setQuery('size', value !== defaultSize ? String(value) : null);
    },
  };
}
