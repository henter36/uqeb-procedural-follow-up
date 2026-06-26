import { afterEach, describe, expect, it, vi } from 'vitest';
import { openHtmlPrintWindow } from './followUpPrintWindow';

describe('openHtmlPrintWindow', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it('opens a sanitized blob URL in a new window', async () => {
    const addEventListener = vi.fn();
    const focus = vi.fn();
    const open = vi.fn(() => ({ addEventListener, focus }));
    const createObjectURL = vi.fn((blob: Blob) => {
      void blob;
      return 'blob:print-test';
    });
    const revokeObjectURL = vi.fn();

    vi.stubGlobal('URL', { createObjectURL, revokeObjectURL });
    vi.stubGlobal('open', open);

    openHtmlPrintWindow('<html><body>print</body></html>');

    expect(createObjectURL).toHaveBeenCalledWith(expect.any(Blob));
    await expect((createObjectURL.mock.calls[0]?.[0] as Blob).text()).resolves.toContain('<body>print</body>');
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

  it('removes executable markup before opening the print window', async () => {
    const addEventListener = vi.fn();
    const focus = vi.fn();
    const open = vi.fn(() => ({ addEventListener, focus }));
    const createObjectURL = vi.fn((blob: Blob) => {
      void blob;
      return 'blob:safe-print';
    });

    vi.stubGlobal('URL', { createObjectURL, revokeObjectURL: vi.fn() });
    vi.stubGlobal('open', open);

    openHtmlPrintWindow(`
      <html>
        <body onload="alert(1)">
          <a href="javascript:alert(1)">bad</a>
          <iframe srcdoc="<script>alert(1)</script>"></iframe>
          <script>alert(1)</script>
          <p>safe</p>
        </body>
      </html>
    `);

    const blob = createObjectURL.mock.calls[0]?.[0] as Blob;
    const safeHtml = await blob.text();

    expect(safeHtml).not.toContain('<script');
    expect(safeHtml).not.toContain('onload=');
    expect(safeHtml).not.toContain('javascript:');
    expect(safeHtml).not.toContain('<iframe');
    expect(safeHtml).toContain('<p>safe</p>');
  });
});
