import { defineConfig } from 'vitest/config';
import { svelte } from '@sveltejs/vite-plugin-svelte';
import { svelteTesting } from '@testing-library/svelte/vite';

const backend = process.env.REEF_BACKEND ?? 'http://localhost:8085';

export default defineConfig(({ command }) => ({
  plugins: [svelte(), svelteTesting()],
  base: command === 'build' ? '/app/' : '/',
  build: { outDir: '../Reef/wwwroot/app', emptyOutDir: true, chunkSizeWarningLimit: 1200 },
  server: { port: 3000, strictPort: true, proxy: { '/api': backend, '/health': backend } },
  test: { environment: 'jsdom', include: ['tests/**/*.test.ts'] },
}));
