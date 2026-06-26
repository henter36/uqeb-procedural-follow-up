import { describe, expect, it } from 'vitest';
import { sanitizePrintHtml } from './sanitizePrintHtml';

describe('sanitizePrintHtml', () => {
  it('preserves the follow-up print shell and removes executable markup', () => {
    const result = sanitizePrintHtml(`
      <!doctype html>
      <html lang="ar" dir="rtl">
        <head>
          <meta charset="utf-8" />
          <title>خطاب</title>
          <style>.letter { color: #111; }</style>
        </head>
        <body onload="alert(1)">
          <article class="letter">
            <img class="logo" src="/api/branding/organization-logo" alt="" />
            <h1>عنوان</h1>
            <p style="color:red">نص</p>
            <script>alert(1)</script>
            <iframe srcdoc="<script>alert(1)</script>"></iframe>
          </article>
        </body>
      </html>
    `);

    // Document-level wrappers are stripped (WHOLE_DOCUMENT: false — fragment mode).
    expect(result).not.toContain('<html');
    expect(result).not.toContain('<head');
    expect(result).not.toContain('<body');
    // Content inside body is preserved.
    expect(result).toContain('/api/branding/organization-logo');
    expect(result).toContain('<p>نص</p>');
    // Executable markup is removed.
    expect(result).not.toContain('<style');
    expect(result).not.toContain('color:red');
    expect(result).not.toContain('<script');
    expect(result).not.toContain('<iframe');
    expect(result).not.toContain('onload');
    expect(result).not.toContain('srcdoc');
  });

  it('removes javascript URLs from print content', () => {
    const result = sanitizePrintHtml('<html><body><img src="javascript:alert(1)" /><p>safe</p></body></html>');

    expect(result).not.toContain('javascript:');
    expect(result).toContain('<p>safe</p>');
  });

  it('removes inline event handlers while preserving allowed image attributes', () => {
    const result = sanitizePrintHtml('<article><img src="/logo.png" alt="شعار" onerror="alert(1)" /></article>');

    expect(result).toContain('src="/logo.png"');
    expect(result).toContain('alt="شعار"');
    expect(result).not.toContain('onerror');
  });
});
