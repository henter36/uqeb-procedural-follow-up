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
        <head>
          <meta charset="utf-8" />
          <title>خطاب</title>
          <style id="uqeb-official-letter-css">.letter{color:#111;}</style>
        </head>
        <body><article class="letter"><h1>عنوان</h1><p>نص</p></article></body>
      </html>`);

    expect(result).toMatch(/^<!DOCTYPE html>/i);
    expect(result).toContain('<html');
    expect(result).toContain('<head');
    expect(result).toContain('<body');
    expect(result).toContain('<style id="uqeb-official-letter-css">');
    expect(result).toContain('عنوان');
    expect(result).toContain('نص');
  });

  it('preserves only the officially-marked CSS and discards other style elements', () => {
    const result = sanitizeFullDocumentHtml(`<!DOCTYPE html>
      <html><head>
        <style id="uqeb-official-letter-css">.official{color:green;}</style>
        <style>.attacker-css{color:red;}</style>
      </head><body><p>ok</p></body></html>`);

    expect(result).toContain('.official{color:green;}');
    expect(result).not.toContain('.attacker-css');
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
        <style id="uqeb-official-letter-css">.x{color:red;}</style>
      </head><body><p>ok</p></body></html>`);

    expect(result).not.toContain('http-equiv="refresh"');
    expect(result).not.toContain('<base');
    expect(result).toContain('<meta charset');
    expect(result).toContain('<title>safe</title>');
    expect(result).toContain('<style id="uqeb-official-letter-css">');
  });

  it('removes javascript URIs from src attributes', () => {
    const result = sanitizeFullDocumentHtml(
      '<!DOCTYPE html><html><body><img src="javascript:alert(1)" /><p>safe</p></body></html>',
    );

    expect(result).not.toContain('javascript:');
    expect(result).toContain('<p>safe</p>');
  });

  it('removes srcdoc attributes', () => {
    const result = sanitizeFullDocumentHtml(
      '<!DOCTYPE html><html><body><iframe srcdoc="<script>alert(1)</script>"></iframe><p>ok</p></body></html>',
    );

    expect(result).not.toContain('srcdoc');
    expect(result).not.toContain('<iframe');
    expect(result).toContain('<p>ok</p>');
  });

  it('removes xlink:href and namespace attributes', () => {
    const result = sanitizeFullDocumentHtml(
      '<!DOCTYPE html><html><body><svg><use xlink:href="javascript:alert(1)"/></svg><p>ok</p></body></html>',
    );

    expect(result).not.toContain('xlink:href');
    expect(result).not.toContain('javascript:');
  });

  it('removes onclick and other on* handlers', () => {
    const result = sanitizeFullDocumentHtml(
      '<!DOCTYPE html><html><body><div onclick="alert(1)" onmouseover="evil()"><p>ok</p></div></body></html>',
    );

    expect(result).not.toContain('onclick');
    expect(result).not.toContain('onmouseover');
    expect(result).toContain('<p>ok</p>');
  });

  it('reconstructed document does not need allow-scripts in sandbox', () => {
    const result = sanitizeFullDocumentHtml(`<!DOCTYPE html>
      <html><body><p>متن الخطاب</p></body></html>`);

    // The output is safe to embed in an iframe with sandbox="allow-same-origin" (no allow-scripts)
    expect(result).not.toContain('<script');
    expect(result).not.toContain('javascript:');
    expect(result).toContain('<p>متن الخطاب</p>');
  });

  it('returns empty string for empty or null input', () => {
    expect(sanitizeFullDocumentHtml('')).toBe('');
    expect(sanitizeFullDocumentHtml(null as unknown as string)).toBe('');
  });
});
