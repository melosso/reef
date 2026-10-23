export interface ConfirmRequest {
  title: string;
  message?: string;
  confirm?: string;
  danger?: boolean;
  typed?: string;
}

export const pending = $state<{ request: ConfirmRequest | null; resolve: ((ok: boolean) => void) | null }>({
  request: null,
  resolve: null,
});

export function confirm(request: ConfirmRequest): Promise<boolean> {
  pending.resolve?.(false);
  return new Promise((resolve) => {
    pending.request = request;
    pending.resolve = resolve;
  });
}

export function settle(ok: boolean): void {
  pending.resolve?.(ok);
  pending.request = null;
  pending.resolve = null;
}
