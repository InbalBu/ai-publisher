import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      // Lets the app always call relative /api/... URLs in both dev and
      // production, so no CORS configuration is needed on the API at all.
      '/api': {
        target: 'http://localhost:5199',
        changeOrigin: true,
      },
    },
  },
})
