/// <reference types="vitest/config" />
import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// In development the SPA calls relative /api URLs, which Vite proxies to the local API.
// In Docker, Nginx serves the build and proxies /api to the api container, so the
// browser never needs to know an internal host name.
const apiTarget = process.env.VITE_DEV_API_PROXY ?? 'http://localhost:5080'

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      // A remote (https) target needs its own Host header; the local API keeps the browser's.
      '/api': { target: apiTarget, changeOrigin: apiTarget.startsWith('https') },
      '/hubs': { target: apiTarget, ws: true, changeOrigin: apiTarget.startsWith('https') },
      '/swagger': { target: apiTarget },
      '/openapi': { target: apiTarget },
    },
  },
  build: {
    rolldownOptions: {
      output: {
        // Long-term cacheable vendor chunks, separate from application code.
        codeSplitting: {
          groups: [
            { name: 'vendor-react', test: /node_modules[\\/](react|react-dom|scheduler|react-router)[\\/]/ },
            { name: 'vendor-forms', test: /node_modules[\\/](zod|react-hook-form|@hookform)[\\/]/ },
          ],
        },
      },
    },
  },
  test: {
    environment: 'jsdom',
    globals: false,
    setupFiles: ['./src/test/setup.ts'],
    css: false,
    restoreMocks: true,
  },
})
