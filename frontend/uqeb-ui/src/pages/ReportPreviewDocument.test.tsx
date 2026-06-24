import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import { ReportPreviewDocument } from './ReportPreviewDocument';
import { assertSafeStylesheet, buildPreviewDocument } from './reportPreviewHtml';

const sampleHtml = '<section class="report-page"><main>محتوى</main></section>';
const sampleStylesheet = ':root { --report-primary: #123F32; } body { margin: 0; }';

describe('buildPreviewDocument', () => {
  it('rejects unsafe stylesheet breakout', () => {
    expect(() => assertSafeStylesheet('</style><script>alert(1)</script>')).toThrow(/Unsafe stylesheet/);
  });

  it('builds isolated html document with stylesheet and body content', () => {
    const doc = buildPreviewDocument(sampleStylesheet, sampleHtml);
    expect(doc).toContain('<style>:root { --report-primary: #123F32; }');
    expect(doc).toContain(sampleHtml);
    expect(doc).toContain('dir="rtl"');
  });
});

describe('ReportPreviewDocument', () => {
  it('renders sandboxed iframe with sanitized srcDoc and does not inject styles into host document', () => {
    const originalDir = document.body.getAttribute('dir');
    const originalFont = document.body.style.fontFamily;
    const originalBackground = document.body.style.background;

    render(
      <ReportPreviewDocument
        htmlContent={'<script>alert(1)</script><p onclick="x()">نص</p>'}
        stylesheet={sampleStylesheet}
        title="معاينة"
      />,
    );

    const iframe = screen.getByTitle('معاينة');
    expect(iframe.tagName).toBe('IFRAME');
    expect(iframe).toHaveAttribute('sandbox', '');
    expect(iframe.getAttribute('sandbox') ?? '').not.toMatch(/allow-scripts/);

    const srcDoc = iframe.getAttribute('srcDoc') ?? '';
    expect(srcDoc).toContain('--report-primary');
    expect(srcDoc).toContain('نص');
    expect(srcDoc).not.toContain('<script>');
    expect(srcDoc).not.toContain('onclick');

    expect(document.querySelector('style')).toBeNull();
    expect(document.body.getAttribute('dir')).toBe(originalDir);
    expect(document.body.style.fontFamily).toBe(originalFont);
    expect(document.body.style.background).toBe(originalBackground);
  });
});
