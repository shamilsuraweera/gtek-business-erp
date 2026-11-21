import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// The API runs on :5004 in Development (VITE_API_TARGET overrides it, as the smoke test does); proxying avoids CORS and keeps URLs relative.
export default defineConfig({
  plugins: [react()],
  server: {
    port: Number(process.env.VITE_PORT ?? 5173),
    proxy: { '/api': { target: process.env.VITE_API_TARGET ?? 'http://localhost:5004', changeOrigin: true } },
  },
})
