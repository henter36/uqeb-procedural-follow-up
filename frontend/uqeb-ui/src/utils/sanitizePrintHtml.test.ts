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
            <p>نص</p>
            <script>alert(1)</script>
            <iframe srcdoc="<script>alert(1)</script>"></iframe>
          </article>
        </body>
      </html>
    `);

    expect(result).toContain('lang="ar"');
    expect(result).toContain('dir="rtl"');
    expect(result).toContain('<style>.letter { color: #111; }</style>');
    expect(result).toContain('/api/branding/organization-logo');
    expect(result).toContain('<p>نص</p>');
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
});
