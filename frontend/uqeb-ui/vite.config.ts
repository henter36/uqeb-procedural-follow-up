import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

function stripDependencyLoopbackFallbacks() {
  return {
    name: 'strip-dependency-loopback-fallbacks',
    transform(code: string, id: string) {
      if (!id.includes('/node_modules/react-router/dist/')) {
        return null;
      }

      return code.replaceAll('"http://localhost"', '"http://example.invalid"');
    },
  };
}

export default defineConfig({
  plugins: [react(), stripDependencyLoopbackFallbacks()],
  resolve: {
    alias: {
      axios: new URL('./src/api/axiosCompat.ts', import.meta.url).pathname,
    },
  },
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: 'http://localhost:5000',
        changeOrigin: true,
      },
    },
  },
});
