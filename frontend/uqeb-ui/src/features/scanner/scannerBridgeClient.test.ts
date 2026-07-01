import { afterEach, describe, expect, it, vi } from 'vitest';

const runtimeGlobal = globalThis as typeof globalThis & {
  __UQEB_CONFIG__?: { scannerBridgeUrl?: string };
};

describe('scannerBridgeClient', () => {
  afterEach(() => {
    vi.resetModules();
    delete runtimeGlobal.__UQEB_CONFIG__;
    vi.unstubAllEnvs();
    vi.unstubAllGlobals();
  });

  it('uses the loopback scanner bridge by default', async () => {
    const { getScannerBridgeBaseUrl, isScannerConfigured } = await import('./scannerBridgeClient');

    expect(getScannerBridgeBaseUrl()).toBe('http://127.0.0.1:5055');
    expect(isScannerConfigured()).toBe(true);
  });

  it('uses runtime scannerBridgeUrl before build-time configuration', async () => {
    vi.stubEnv('VITE_SCANNER_BRIDGE_URL', 'http://127.0.0.1:5057/');
    runtimeGlobal.__UQEB_CONFIG__ = {
      scannerBridgeUrl: 'http://127.0.0.1:5056/',
    };

    const { getScannerBridgeBaseUrl } = await import('./scannerBridgeClient');

    expect(getScannerBridgeBaseUrl()).toBe('http://127.0.0.1:5056');
  });

  it('uses build-time scannerBridgeUrl when runtime configuration is absent', async () => {
    vi.stubEnv('VITE_SCANNER_BRIDGE_URL', 'http://127.0.0.1:5057/');

    const { getScannerBridgeBaseUrl } = await import('./scannerBridgeClient');

    expect(getScannerBridgeBaseUrl()).toBe('http://127.0.0.1:5057');
  });

  it('trims trailing slashes from runtime scannerBridgeUrl', async () => {
    runtimeGlobal.__UQEB_CONFIG__ = {
      scannerBridgeUrl: ' http://127.0.0.1:5058/// ',
    };

    const { getScannerBridgeBaseUrl } = await import('./scannerBridgeClient');

    expect(getScannerBridgeBaseUrl()).toBe('http://127.0.0.1:5058');
  });
});
