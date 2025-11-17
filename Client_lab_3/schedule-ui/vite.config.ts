import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/api/schedules': {
        target: 'http://localhost:5001',
        changeOrigin: true,
      },
      '/api/catalog': {
        target: 'http://localhost:5005',
        changeOrigin: true,
      },
      '/api/analytics': {
        target: 'http://localhost:5004',
        changeOrigin: true,
      },
      '/api/optimization': {
        target: 'http://localhost:5002',
        changeOrigin: true,
      }
    }
  }
});