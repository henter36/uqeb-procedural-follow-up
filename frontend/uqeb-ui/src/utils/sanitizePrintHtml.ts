import DOMPurify from 'dompurify';

const PRINT_HTML_ALLOWED_TAGS = [
  'html', 'head', 'body', 'meta', 'title', 'style',
  'article', 'header', 'section', 'footer', 'div', 'span',
  'h1', 'h2', 'h3', 'p', 'strong', 'em', 'b', 'i', 'br',
  'table', 'thead', 'tbody', 'tfoot', 'tr', 'th', 'td',
  'img',
] as const;

const PRINT_HTML_ALLOWED_ATTR = [
  'class', 'id', 'dir', 'lang', 'charset', 'name', 'content',
  'aria-hidden', 'alt', 'src', 'width', 'height', 'style',
  'colspan', 'rowspan', 'scope',
] as const;

export function sanitizePrintHtml(html: string): string {
  if (!html) return '';

  return DOMPurify.sanitize(html, {
    WHOLE_DOCUMENT: true,
    ALLOWED_TAGS: [...PRINT_HTML_ALLOWED_TAGS],
    ALLOWED_ATTR: [...PRINT_HTML_ALLOWED_ATTR],
    ALLOW_DATA_ATTR: false,
    FORBID_TAGS: ['script', 'iframe', 'object', 'embed', 'form', 'input', 'button', 'link', 'base'],
    FORBID_ATTR: ['onerror', 'onclick', 'onload', 'onmouseover', 'onfocus', 'onblur', 'srcdoc'],
    ALLOWED_URI_REGEXP: /^(?:(?:https?):|\/|[^a-z]|[a-z+.-]+(?:[^a-z+.\-:]|$))/i,
  });
}
