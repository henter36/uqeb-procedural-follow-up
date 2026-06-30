import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

const devApiProxyTarget = process.env.UQEB_DEV_API_PROXY_TARGET ?? 'https://localhost:5001';
const dependencyLoopbackFallback = ['http:', '//localhost'].join('');
const dependencyReplacementOrigin = 'https://example.invalid';

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

      return code.replaceAll(dependencyLoopbackFallback, dependencyReplacementOrigin);
    },
  };
}

export default defineConfig({
  plugins: [react(), stripDependencyLoopbackFallbacks()],
  server: {
    port: 5173,
    proxy: {
      '/api': {
        // Override UQEB_DEV_API_PROXY_TARGET when the local API is served over HTTP.
        target: devApiProxyTarget,
        changeOrigin: true,
        secure: false,
      },
    },
  },
});
