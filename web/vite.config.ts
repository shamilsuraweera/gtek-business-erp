import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// The API runs on :5004 in Development; proxying avoids CORS and keeps URLs relative.
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: { '/api': { target: 'http://localhost:5004', changeOrigin: true } },
  },
})
