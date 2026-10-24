export const commandBar = $state({ open: false });

export function openCommandBar(): void {
  commandBar.open = true;
}
