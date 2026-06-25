import { afterEach, describe, expect, it, vi } from 'vitest';
import { createIdempotencyKey } from './createIdempotencyKey';

const UUID_V4_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

describe('createIdempotencyKey', () => {
  afterEach(() => {
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
  });

  it('uses crypto.randomUUID when available', () => {
    vi.stubGlobal('crypto', {
      randomUUID: vi.fn(() => '11111111-1111-4111-8111-111111111111'),
    });

    expect(createIdempotencyKey()).toBe('11111111-1111-4111-8111-111111111111');
    expect(crypto.randomUUID).toHaveBeenCalledOnce();
  });

  it('builds a UUID v4 from getRandomValues when randomUUID is unavailable', () => {
    const bytes = Uint8Array.from({ length: 16 }, (_, index) => index);
    vi.stubGlobal('crypto', {
      getRandomValues: vi.fn((target: Uint8Array) => {
        target.set(bytes);
        return target;
      }),
    });

    const key = createIdempotencyKey();
    expect(key).toMatch(UUID_V4_PATTERN);
    expect(key).toBe('00010203-0405-4607-8809-0a0b0c0d0e0f');
  });

  it('falls back when crypto APIs are unavailable', () => {
    vi.stubGlobal('crypto', undefined);

    const key = createIdempotencyKey();
    expect(key).toMatch(/^[a-z0-9]+-[a-z0-9]+$/i);
    expect(key).not.toMatch(UUID_V4_PATTERN);
  });
});
