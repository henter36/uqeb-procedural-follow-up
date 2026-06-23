import DOMPurify from 'dompurify';

const REPORT_HTML_ALLOWED_TAGS = [
  'section', 'main', 'header', 'footer', 'article', 'div', 'span', 'p',
  'h1', 'h2', 'h3', 'h4', 'h5', 'h6',
  'table', 'thead', 'tbody', 'tfoot', 'tr', 'th', 'td',
  'ul', 'ol', 'li', 'dl', 'dt', 'dd',
  'strong', 'em', 'b', 'i', 'small', 'br', 'hr',
  'figure', 'figcaption', 'img', 'svg', 'path', 'circle', 'rect', 'line', 'text', 'g',
] as const;

const REPORT_HTML_ALLOWED_ATTR = [
  'class', 'dir', 'lang', 'colspan', 'rowspan', 'scope',
  'data-page', 'data-section', 'aria-hidden', 'role',
  'width', 'height', 'viewBox', 'fill', 'stroke', 'd', 'x', 'y', 'cx', 'cy', 'r',
  'x1', 'y1', 'x2', 'y2', 'style', 'alt',
] as const;

/** Sanitize institutional report preview HTML while preserving report layout elements. */
export function sanitizeReportHtml(html: string): string {
  if (!html) return '';

  return DOMPurify.sanitize(html, {
    ALLOWED_TAGS: [...REPORT_HTML_ALLOWED_TAGS],
    ALLOWED_ATTR: [...REPORT_HTML_ALLOWED_ATTR],
    ALLOW_DATA_ATTR: true,
    FORBID_TAGS: ['script', 'iframe', 'object', 'embed', 'form', 'input', 'button', 'link', 'meta', 'base'],
    FORBID_ATTR: ['onerror', 'onclick', 'onload', 'onmouseover', 'onfocus', 'onblur'],
    ALLOWED_URI_REGEXP: /^(?:(?:https?|mailto|tel):|[^a-z]|[a-z+.-]+(?:[^a-z+.\-:]|$))/i,
  });
}
