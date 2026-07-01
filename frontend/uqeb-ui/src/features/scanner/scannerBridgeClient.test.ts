import { afterEach, describe, expect, it, vi } from 'vitest';

describe('scannerBridgeClient', () => {
  afterEach(() => {
    vi.resetModules();
    vi.unstubAllGlobals();
  });

  it('uses the loopback scanner bridge by default', async () => {
    const { getScannerBridgeBaseUrl, isScannerConfigured } = await import('./scannerBridgeClient');

    expect(getScannerBridgeBaseUrl()).toBe('http://127.0.0.1:5055');
    expect(isScannerConfigured()).toBe(true);
  });

  it('uses runtime scannerBridgeUrl before build-time configuration', async () => {
    vi.stubGlobal('__UQEB_CONFIG__', {
      scannerBridgeUrl: 'http://127.0.0.1:5056/',
    });

    const { getScannerBridgeBaseUrl } = await import('./scannerBridgeClient');

    expect(getScannerBridgeBaseUrl()).toBe('http://127.0.0.1:5056');
  });
});
