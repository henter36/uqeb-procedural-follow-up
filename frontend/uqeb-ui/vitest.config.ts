import { defineConfig, mergeConfig } from 'vitest/config';
import viteConfig from './vite.config';

export default mergeConfig(viteConfig, defineConfig({
  test: {
    environment: 'jsdom',
    setupFiles: ['./src/test/setup.ts'],
    exclude: [
      '**/node_modules/**',
      '**/dist/**',
      '**/.claude/**',
    ],
    env: {
      // Ensure feature is enabled by default in tests regardless of .env.local overrides.
      // Tests that need it disabled call vi.stubEnv('VITE_ENABLE_INSTITUTIONAL_REPORTS', 'false') explicitly.
      VITE_ENABLE_INSTITUTIONAL_REPORTS: 'true',
    },
  },
}));
