import { defineConfig } from 'vite';
import { svelte } from '@sveltejs/vite-plugin-svelte';

const backend = process.env.REEF_BACKEND ?? 'http://localhost:8085';

export default defineConfig(({ command }) => ({
  plugins: [svelte()],
  base: command === 'build' ? '/app/' : '/',
  build: { outDir: '../Reef/wwwroot/app', emptyOutDir: true, chunkSizeWarningLimit: 1200 },
  server: { port: 3000, strictPort: true, proxy: { '/api': backend, '/health': backend } }
}));
