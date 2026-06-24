import { afterEach, describe, expect, it, vi } from 'vitest';
import { buildNavSections } from '../components/layout/navConfig';
import { isInstitutionalReportsEnabled } from './featureFlags';

describe('isInstitutionalReportsEnabled', () => {
  afterEach(() => {
    vi.unstubAllEnvs();
    vi.resetModules();
  });

  it('is disabled by default when env var is false', async () => {
    vi.stubEnv('VITE_ENABLE_INSTITUTIONAL_REPORTS', 'false');
    vi.resetModules();
    const { isInstitutionalReportsEnabled: enabled } = await import('./featureFlags');
    expect(enabled()).toBe(false);
  });

  it('is enabled when env var is true', async () => {
    vi.stubEnv('VITE_ENABLE_INSTITUTIONAL_REPORTS', 'true');
    vi.resetModules();
    const { isInstitutionalReportsEnabled: enabled } = await import('./featureFlags');
    expect(enabled()).toBe(true);
  });
});

describe('institutional reports navigation', () => {
  afterEach(() => {
    vi.unstubAllEnvs();
    vi.resetModules();
  });

  function reportBuilderPaths(sections: ReturnType<typeof buildNavSections>) {
    return sections.flatMap((section) => section.items.map((item) => item.path));
  }

  it('hides report builder nav link when feature flag is disabled', async () => {
    vi.stubEnv('VITE_ENABLE_INSTITUTIONAL_REPORTS', 'false');
    vi.resetModules();
    const { buildNavSections: buildSections } = await import('../components/layout/navConfig');
    const paths = reportBuilderPaths(buildSections());
    expect(paths).not.toContain('/report-builder');
  });

  it('shows report builder nav link when feature flag is enabled', async () => {
    vi.stubEnv('VITE_ENABLE_INSTITUTIONAL_REPORTS', 'true');
    vi.resetModules();
    const { buildNavSections: buildSections } = await import('../components/layout/navConfig');
    const paths = reportBuilderPaths(buildSections());
    expect(paths).toContain('/report-builder');
  });

  it('uses the same gate as App route registration', () => {
    vi.stubEnv('VITE_ENABLE_INSTITUTIONAL_REPORTS', 'true');
    expect(isInstitutionalReportsEnabled()).toBe(true);
    vi.stubEnv('VITE_ENABLE_INSTITUTIONAL_REPORTS', 'false');
    expect(isInstitutionalReportsEnabled()).toBe(false);
  });
});
