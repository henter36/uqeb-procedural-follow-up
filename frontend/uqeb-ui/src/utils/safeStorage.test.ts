import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { getStorageItem, setStorageItem, removeStorageItem } from './safeStorage';

describe('safeStorage', () => {
  beforeEach(() => {
    vi.stubGlobal('localStorage', {
      getItem: vi.fn(() => 'value'),
      setItem: vi.fn(),
      removeItem: vi.fn(),
    });
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('reads from localStorage safely', () => {
    expect(getStorageItem('key')).toBe('value');
  });

  it('returns null when getItem throws', () => {
    vi.mocked(localStorage.getItem).mockImplementation(() => {
      throw new Error('quota');
    });
    expect(getStorageItem('key')).toBeNull();
  });

  it('returns false when setItem throws', () => {
    vi.mocked(localStorage.setItem).mockImplementation(() => {
      throw new Error('quota');
    });
    expect(setStorageItem('key', 'x')).toBe(false);
  });

  it('returns false when removeItem throws', () => {
    vi.mocked(localStorage.removeItem).mockImplementation(() => {
      throw new Error('restricted');
    });
    expect(removeStorageItem('key')).toBe(false);
  });

  it('returns null when window is undefined', () => {
    vi.stubGlobal('window', undefined);
    expect(getStorageItem('key')).toBeNull();
  });
});
