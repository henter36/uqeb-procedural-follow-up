import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { getStorageItem, setStorageItem, removeStorageItem } from './safeStorage';

function stubWindowStorage(storage: Storage | null) {
  vi.stubGlobal('window', storage ? { localStorage: storage } : undefined);
}

describe('safeStorage', () => {
  const mockStorage = {
    getItem: vi.fn(() => 'value'),
    setItem: vi.fn(),
    removeItem: vi.fn(),
  };

  beforeEach(() => {
    vi.clearAllMocks();
    mockStorage.getItem.mockReturnValue('value');
    stubWindowStorage(mockStorage as unknown as Storage);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it('reads from localStorage safely', () => {
    expect(getStorageItem('key')).toBe('value');
    expect(mockStorage.getItem).toHaveBeenCalledWith('key');
  });

  it('writes to localStorage safely', () => {
    expect(setStorageItem('key', 'next')).toBe(true);
    expect(mockStorage.setItem).toHaveBeenCalledWith('key', 'next');
  });

  it('removes from localStorage safely', () => {
    expect(removeStorageItem('key')).toBe(true);
    expect(mockStorage.removeItem).toHaveBeenCalledWith('key');
  });

  it('returns null when storage is unavailable', () => {
    stubWindowStorage(null);
    expect(getStorageItem('key')).toBeNull();
    expect(setStorageItem('key', 'x')).toBe(false);
    expect(removeStorageItem('key')).toBe(false);
  });

  it('returns null when window is unavailable', () => {
    vi.stubGlobal('window', undefined);
    expect(() => getStorageItem('key')).not.toThrow();
    expect(getStorageItem('key')).toBeNull();
    expect(() => setStorageItem('key', 'x')).not.toThrow();
    expect(setStorageItem('key', 'x')).toBe(false);
    expect(() => removeStorageItem('key')).not.toThrow();
    expect(removeStorageItem('key')).toBe(false);
  });

  it('returns null when getItem throws', () => {
    mockStorage.getItem.mockImplementation(() => {
      throw new Error('quota');
    });
    expect(() => getStorageItem('key')).not.toThrow();
    expect(getStorageItem('key')).toBeNull();
  });

  it('returns false when setItem throws', () => {
    mockStorage.setItem.mockImplementation(() => {
      throw new Error('quota');
    });
    expect(() => setStorageItem('key', 'x')).not.toThrow();
    expect(setStorageItem('key', 'x')).toBe(false);
  });

  it('returns false when removeItem throws', () => {
    mockStorage.removeItem.mockImplementation(() => {
      throw new Error('restricted');
    });
    expect(() => removeStorageItem('key')).not.toThrow();
    expect(removeStorageItem('key')).toBe(false);
  });

  it('returns null when localStorage access throws during lookup', () => {
    vi.stubGlobal('window', {
      get localStorage() {
        throw new Error('blocked');
      },
    });
    expect(() => getStorageItem('key')).not.toThrow();
    expect(getStorageItem('key')).toBeNull();
  });
});
