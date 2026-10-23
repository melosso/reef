export type ThemeChoice = 'system' | 'light' | 'dark';

function initial(): ThemeChoice {
  try {
    const value = localStorage.getItem('reef_theme');
    return value === 'light' || value === 'dark' ? value : 'system';
  } catch {
    return 'system';
  }
}

const media = matchMedia('(prefers-color-scheme: dark)');

export const theme = $state({ choice: initial(), systemDark: media.matches });

media.addEventListener('change', (e) => (theme.systemDark = e.matches));

export const isDark = (): boolean => theme.choice === 'dark' || (theme.choice === 'system' && theme.systemDark);

export function setTheme(choice: ThemeChoice): void {
  theme.choice = choice;
  if (choice === 'system') delete document.documentElement.dataset.theme;
  else document.documentElement.dataset.theme = choice;
  try {
    if (choice === 'system') localStorage.removeItem('reef_theme');
    else localStorage.setItem('reef_theme', choice);
  } catch {}
}
