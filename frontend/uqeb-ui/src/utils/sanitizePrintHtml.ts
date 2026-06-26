import DOMPurify, { type Config } from 'dompurify';

const PRINT_HTML_ALLOWED_TAGS = [
  'article', 'header', 'section', 'footer', 'div', 'span',
  'h1', 'h2', 'h3', 'p', 'strong', 'em', 'b', 'i', 'br',
  'table', 'thead', 'tbody', 'tfoot', 'tr', 'th', 'td',
  'img',
] as const;

const PRINT_HTML_ALLOWED_ATTR = [
  'class', 'id', 'dir', 'lang',
  'aria-hidden', 'alt', 'src', 'width', 'height',
  'colspan', 'rowspan', 'scope',
] as const;

const OPTIONS: Config = {
  WHOLE_DOCUMENT: false,
  ALLOW_DATA_ATTR: false,
  ALLOWED_TAGS: [...PRINT_HTML_ALLOWED_TAGS],
  ALLOWED_ATTR: [...PRINT_HTML_ALLOWED_ATTR],
  FORBID_TAGS: ['style', 'script', 'iframe', 'object', 'embed', 'form', 'input', 'button', 'link', 'base'],
  FORBID_ATTR: ['style', 'onerror', 'onclick', 'onload', 'onmouseover', 'onfocus', 'onblur', 'srcdoc'],
  ALLOWED_URI_REGEXP: /^(?:(?:https?):|\/|[^a-z]|[a-z+.-]+(?:[^a-z+.\-:]|$))/i,
};

export function sanitizePrintHtml(html: string): string {
  if (!html) return '';

  const doc = new DOMParser().parseFromString(html, 'text/html');

  return DOMPurify.sanitize(doc.body.innerHTML, OPTIONS);
}

export function sanitizeFullDocumentHtml(html: string): string {
  if (!html) return '';

  // First pass: DOMPurify removes scripts, xlink:href, on* attributes, javascript: URIs
  const sanitized = DOMPurify.sanitize(html, {
    WHOLE_DOCUMENT: true,
    FORCE_BODY: false,
    ALLOWED_TAGS: [
      'html', 'head', 'body', 'meta', 'style', 'title',
      'article', 'header', 'section', 'footer', 'div', 'span',
      'h1', 'h2', 'h3', 'h4', 'p', 'strong', 'em', 'b', 'i', 'br',
      'table', 'thead', 'tbody', 'tfoot', 'tr', 'th', 'td', 'img',
    ],
    ALLOWED_ATTR: [
      'class', 'id', 'dir', 'lang',
      'aria-hidden', 'alt', 'src', 'width', 'height',
      'colspan', 'rowspan', 'scope',
      'charset', 'name', 'content', 'http-equiv',
    ],
  });

  // Second pass: strip remaining navigation-capable head elements
  const doc = new DOMParser().parseFromString(sanitized, 'text/html');
  Array.from(doc.head.children).forEach((el) => {
    const keep = el.matches('style, title, meta[charset], meta[name="viewport"]');
    if (!keep) el.remove();
  });

  return `<!DOCTYPE html>${doc.documentElement.outerHTML}`;
}
