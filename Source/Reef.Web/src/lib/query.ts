export function queryString(params: Record<string, string | number | null | undefined>): string {
  const entries = Object.entries(params).filter(([, value]) => value !== null && value !== undefined && value !== '');
  return new URLSearchParams(entries.map(([key, value]) => [key, String(value)])).toString();
}
