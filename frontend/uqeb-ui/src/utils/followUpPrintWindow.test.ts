import { afterEach, describe, expect, it, vi } from 'vitest';
import { openHtmlPrintWindow } from './followUpPrintWindow';

describe('openHtmlPrintWindow', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it('opens a blob URL in a new window', () => {
    const addEventListener = vi.fn();
    const focus = vi.fn();
    const open = vi.fn(() => ({ addEventListener, focus }));
    const createObjectURL = vi.fn(() => 'blob:print-test');
    const revokeObjectURL = vi.fn();

    vi.stubGlobal('URL', { createObjectURL, revokeObjectURL });
    vi.stubGlobal('open', open);

    openHtmlPrintWindow('<html><body>print</body></html>');

    expect(createObjectURL).toHaveBeenCalledWith(expect.any(Blob));
    expect(open).toHaveBeenCalledWith('blob:print-test', '_blank', 'noopener,noreferrer');
    expect(addEventListener).toHaveBeenCalledWith('load', expect.any(Function), { once: true });
    expect(focus).toHaveBeenCalled();

    const onLoad = addEventListener.mock.calls[0]?.[1] as () => void;
    onLoad();
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:print-test');
  });

  it('throws and revokes the blob URL when popup is blocked', () => {
    const createObjectURL = vi.fn(() => 'blob:blocked');
    const revokeObjectURL = vi.fn();

    vi.stubGlobal('URL', { createObjectURL, revokeObjectURL });
    vi.stubGlobal('open', vi.fn(() => null));

    expect(() => openHtmlPrintWindow('<html></html>')).toThrow('تعذر فتح نافذة الطباعة');
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:blocked');
  });
});
