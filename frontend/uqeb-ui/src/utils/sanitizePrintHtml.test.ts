import { describe, expect, it } from 'vitest';
import { sanitizeFullDocumentHtml, sanitizePrintHtml } from './sanitizePrintHtml';

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

describe('sanitizeFullDocumentHtml', () => {
  it('returns a full HTML document with DOCTYPE', () => {
    const result = sanitizeFullDocumentHtml(`<!DOCTYPE html>
      <html lang="ar" dir="rtl">
        <head><meta charset="utf-8" /><title>خطاب</title><style>.letter{color:#111;}</style></head>
        <body><article class="letter"><h1>عنوان</h1><p>نص</p></article></body>
      </html>`);

    expect(result).toMatch(/^<!DOCTYPE html>/i);
    expect(result).toContain('<html');
    expect(result).toContain('<head');
    expect(result).toContain('<body');
    expect(result).toContain('<style');
    expect(result).toContain('عنوان');
    expect(result).toContain('نص');
  });

  it('removes script tags and on* attributes', () => {
    const result = sanitizeFullDocumentHtml(`<!DOCTYPE html>
      <html><body onload="alert(1)"><script>alert(1)</script><p>safe</p></body></html>`);

    expect(result).not.toContain('<script');
    expect(result).not.toContain('onload');
    expect(result).toContain('<p>safe</p>');
  });

  it('strips meta refresh and base tags from head', () => {
    const result = sanitizeFullDocumentHtml(`<!DOCTYPE html>
      <html><head>
        <meta charset="utf-8" />
        <meta http-equiv="refresh" content="0;url=https://evil.example" />
        <base href="https://evil.example" />
        <title>safe</title>
        <style>.x{color:red;}</style>
      </head><body><p>ok</p></body></html>`);

    expect(result).not.toContain('http-equiv="refresh"');
    expect(result).not.toContain('<base');
    expect(result).toContain('<meta charset');
    expect(result).toContain('<title>safe</title>');
    expect(result).toContain('<style');
  });

  it('removes javascript URIs from src attributes', () => {
    const result = sanitizeFullDocumentHtml(
      '<!DOCTYPE html><html><body><img src="javascript:alert(1)" /><p>safe</p></body></html>',
    );

    expect(result).not.toContain('javascript:');
    expect(result).toContain('<p>safe</p>');
  });

  it('returns empty string for empty input', () => {
    expect(sanitizeFullDocumentHtml('')).toBe('');
    expect(sanitizeFullDocumentHtml(null as unknown as string)).toBe('');
  });
});
