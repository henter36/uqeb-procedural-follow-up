import DOMPurify from 'dompurify';

const PRINT_HTML_ALLOWED_TAGS = [
  'article', 'header', 'section', 'footer', 'div', 'span',
  'h1', 'h2', 'h3', 'p', 'strong', 'em', 'b', 'i', 'br',
  'table', 'thead', 'tbody', 'tfoot', 'tr', 'th', 'td',
  'img',
] as const;

const PRINT_HTML_ALLOWED_ATTR = [
  'class', 'id', 'dir', 'lang',
  'aria-hidden', 'alt', 'src', 'width', 'height', 'style',
  'colspan', 'rowspan', 'scope',
] as const;

const SHARED_OPTIONS: DOMPurify.Config = {
  WHOLE_DOCUMENT: false,
  ALLOW_DATA_ATTR: false,
  FORBID_TAGS: ['script', 'iframe', 'object', 'embed', 'form', 'input', 'button', 'link', 'base'],
  FORBID_ATTR: ['onerror', 'onclick', 'onload', 'onmouseover', 'onfocus', 'onblur', 'srcdoc'],
  ALLOWED_URI_REGEXP: /^(?:(?:https?):|\/|[^a-z]|[a-z+.-]+(?:[^a-z+.\-:]|$))/i,
};

export function sanitizePrintHtml(html: string): string {
  if (!html) return '';

  // Use DOMParser to split document into head styles and body content so both
  // can be sanitized in fragment mode (WHOLE_DOCUMENT: false), which avoids
  // XSS vectors that WHOLE_DOCUMENT: true can introduce via document-level elements.
  const doc = new DOMParser().parseFromString(html, 'text/html');

  const safeStyles = Array.from(doc.head.querySelectorAll('style'))
    .map((el) => DOMPurify.sanitize(el.outerHTML, {
      ...SHARED_OPTIONS,
      FORCE_BODY: true,
      ALLOWED_TAGS: ['style'],
      ALLOWED_ATTR: [],
    }))
    .join('\n');

  const safeBody = DOMPurify.sanitize(doc.body.innerHTML, {
    ...SHARED_OPTIONS,
    ALLOWED_TAGS: [...PRINT_HTML_ALLOWED_TAGS],
    ALLOWED_ATTR: [...PRINT_HTML_ALLOWED_ATTR],
  });

  return safeStyles + safeBody;
}
