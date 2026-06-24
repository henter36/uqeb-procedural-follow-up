import { describe, expect, it } from 'vitest';
import { sanitizeReportHtml } from './sanitizeReportHtml';

describe('sanitizeReportHtml', () => {
  it('removes script tags', () => {
    const result = sanitizeReportHtml('<section><script>alert(1)</script><p>نص</p></section>');
    expect(result).not.toContain('<script');
    expect(result).toContain('نص');
  });

  it('removes onerror handlers', () => {
    const result = sanitizeReportHtml('<img src="x" onerror="alert(1)" alt="صورة" />');
    expect(result).not.toContain('onerror');
  });

  it('removes onclick handlers', () => {
    const result = sanitizeReportHtml('<div onclick="steal()">محتوى</div>');
    expect(result).not.toContain('onclick');
    expect(result).toContain('محتوى');
  });

  it('removes javascript: links', () => {
    const result = sanitizeReportHtml('<a href="javascript:alert(1)">رابط</a>');
    expect(result).not.toContain('javascript:');
  });

  it('preserves tables headings and cards', () => {
    const html = `
      <section dir="rtl">
        <h2 class="section-title">الملخص</h2>
        <div class="kpi-card"><span class="label">إجمالي</span><span class="value">10</span></div>
        <table class="report-table"><thead><tr><th>الإدارة</th></tr></thead><tbody><tr><td>الشؤون</td></tr></tbody></table>
      </section>`;
    const result = sanitizeReportHtml(html);
    expect(result).toContain('section-title');
    expect(result).toContain('kpi-card');
    expect(result).toContain('<table');
    expect(result).toContain('الشؤون');
    expect(result).toContain('dir="rtl"');
  });

  it('blocks iframe embed and object', () => {
    const result = sanitizeReportHtml('<iframe src="https://evil.test"></iframe><object data="x"></object><embed />');
    expect(result).not.toContain('iframe');
    expect(result).not.toContain('object');
    expect(result).not.toContain('embed');
  });
});
