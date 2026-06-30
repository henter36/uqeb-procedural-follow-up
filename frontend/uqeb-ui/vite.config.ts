import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

function stripDependencyLoopbackFallbacks() {
  return {
    name: 'strip-dependency-loopback-fallbacks',
    transform(code: string, id: string) {
      const isKnownDependencyFallback =
        id.includes('/node_modules/react-router/dist/')
        || id.includes('/node_modules/axios/');

      if (!isKnownDependencyFallback) {
        return null;
      }

      return code.replaceAll('http://localhost', 'http://example.invalid');
    },
  };
}

export default defineConfig({
  plugins: [react(), stripDependencyLoopbackFallbacks()],
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
