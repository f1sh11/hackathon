import { defineConfig, loadEnv } from 'vite';
import react from '@vitejs/plugin-react';
export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, '.', 'ROAM_');
  const apiOrigin = env.ROAM_API_ORIGIN || 'http://127.0.0.1:5080';
  return {
    plugins: [react()],
    server: { proxy: { '/api': apiOrigin, '/health': apiOrigin } },
    build: { outDir: 'dist', emptyOutDir: true },
  };
});
