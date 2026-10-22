/** 开发服务器配置：同源 API 代理到 ASP.NET。 / Development server configuration: proxies same-origin API requests to ASP.NET. */
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  server: {
    strictPort: true,
    port: 5173,
    proxy: {
      '/api': {
        target: 'http://127.0.0.1:8787',
        changeOrigin: true,
        rewrite: (path) => path.replace(/^\/api/, ''),
        timeout: 250_000,
        proxyTimeout: 250_000,
      },
    },
  },
});
