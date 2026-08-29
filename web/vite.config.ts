import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'

export default defineConfig({
  plugins: [vue()],
  server: {
    port: 5173,
    // The API runs as a separate process in development. Proxying keeps the browser on one
    // origin, so cookies, relative URLs and CORS behave exactly as they do in production,
    // where both are served from the same host.
    proxy: {
      '/v1': { target: 'http://localhost:5272', changeOrigin: true },
      '/docs': { target: 'http://localhost:5272', changeOrigin: true },
      '/openapi': { target: 'http://localhost:5272', changeOrigin: true },
      '/health': { target: 'http://localhost:5272', changeOrigin: true },
    },
  },
  build: {
    // Emitted straight into the API's wwwroot so a single container serves UI and API.
    outDir: '../src/PdfWerk.Api/wwwroot',
    emptyOutDir: true,
  },
})
