import { match, navigate, route } from './router.svelte';

interface Handlers {
  create: () => void;
  edit: (id: number) => void;
  dismiss: () => void;
}

// Opens the record sheet for /{base}/new and /{base}/{id}. Query values such as filters stay in place.
export function detailRoute(base: string, handlers: Handlers) {
  let last: string | null = null;

  $effect(() => {
    const selected = match(`${base}/:id`, route.path)?.id ?? null;
    if (selected === last) return;
    const previous = last;
    last = selected;
    if (selected === 'new') handlers.create();
    else if (selected) handlers.edit(Number(selected));
    else if (previous) handlers.dismiss();
  });

  return {
    open: (id: number | 'new') => navigate(`${base}/${id}${location.search}`),
    close: () => navigate(`${base}${location.search}`),
  };
}
