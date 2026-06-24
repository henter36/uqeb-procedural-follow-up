import { describe, expect, it } from 'vitest';
import { assertSafeStylesheet } from './reportPreviewHtml';

describe('assertSafeStylesheet', () => {
  it.each([
    '</style>',
    '</STYLE>',
    'body { color:red }</style>',
    '</style x>',
    '</style\n>',
  ])('rejects unsafe closing style tag: %s', (stylesheet) => {
    expect(() => assertSafeStylesheet(stylesheet)).toThrow(/Unsafe stylesheet/);
  });

  it.each([
    'body { color: red; }',
    '.report-page { display: block; }',
    'content: "<style>";',
    'style { color: red; }',
    '< /style>',
    '/* style */',
    '',
  ])('accepts safe stylesheet content: %s', (stylesheet) => {
    expect(() => assertSafeStylesheet(stylesheet)).not.toThrow();
  });

  it('completes on a large stylesheet without regex backtracking', () => {
    const stylesheet = `.${'a'.repeat(1_000_000)} { color: red; }`;
    expect(() => assertSafeStylesheet(stylesheet)).not.toThrow();
  });
});
