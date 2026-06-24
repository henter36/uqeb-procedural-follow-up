import { afterEach, describe, expect, it, vi } from 'vitest';

describe('report builder route registration', () => {
  afterEach(() => {
    vi.unstubAllEnvs();
    vi.resetModules();
  });

  it('enables report builder route when feature flag is true', async () => {
    vi.stubEnv('VITE_ENABLE_INSTITUTIONAL_REPORTS', 'true');
    vi.resetModules();
    const { institutionalReportsEnabled } = await import('../App');
    expect(institutionalReportsEnabled).toBe(true);
  });

  it('disables report builder route when feature flag is false', async () => {
    vi.stubEnv('VITE_ENABLE_INSTITUTIONAL_REPORTS', 'false');
    vi.resetModules();
    const { institutionalReportsEnabled } = await import('../App');
    expect(institutionalReportsEnabled).toBe(false);
  });
});
