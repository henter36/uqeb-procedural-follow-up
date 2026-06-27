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

// Permit only http(s) absolute URLs and root-relative paths; blocks javascript: and data: URIs.
const SAFE_URI_REGEXP = /^(?:(?:https?):|\/|[^a-z]|[a-z+.-]+(?:[^a-z+.:-]|$))/i;

const OPTIONS: Config = {
  WHOLE_DOCUMENT: false,
  ALLOW_DATA_ATTR: false,
  ALLOWED_TAGS: [...PRINT_HTML_ALLOWED_TAGS],
  ALLOWED_ATTR: [...PRINT_HTML_ALLOWED_ATTR],
  FORBID_TAGS: ['style', 'script', 'iframe', 'object', 'embed', 'form', 'input', 'button', 'link', 'base'],
  FORBID_ATTR: ['style', 'srcdoc', 'xlink:href',
    'onerror', 'onclick', 'onload', 'onmouseover', 'onfocus', 'onblur',
    'onchange', 'onsubmit', 'onkeydown', 'onkeyup', 'onkeypress'],
  ALLOWED_URI_REGEXP: SAFE_URI_REGEXP,
};

export function sanitizePrintHtml(html: string): string {
  if (!html) return '';

  const doc = new DOMParser().parseFromString(html, 'text/html');

  return DOMPurify.sanitize(doc.body.innerHTML, OPTIONS);
}

// Only for trusted server-generated print HTML. Do not use for user-authored HTML.
export function sanitizeFullDocumentHtml(html: string): string {
  if (!html) return '';

  const doc = new DOMParser().parseFromString(html, 'text/html');

  // Extract only the server-marked official CSS; discard everything else from <head>
  const officialStyleEl = doc.head.querySelector('style#uqeb-official-letter-css');
  const rawCss = officialStyleEl?.textContent ?? '';
  // Prevent premature </style> closing via injected content
  const officialCss = rawCss.replace(/<\/style/gi, String.raw`<\/style`);

  const docTitle = DOMPurify.sanitize(doc.title);

  // Sanitize body content with strict rules — no style, meta, script, or dangerous attrs allowed
  const safeBody = DOMPurify.sanitize(doc.body.innerHTML, OPTIONS);

  // Reconstruct document — we own every element in <head>
  const parts: string[] = [
    '<!DOCTYPE html>',
    '<html lang="ar" dir="rtl">',
    '<head>',
    '<meta charset="utf-8" />',
    '<meta name="viewport" content="width=device-width, initial-scale=1" />',
  ];
  if (docTitle) parts.push(`<title>${docTitle}</title>`);
  if (officialCss) parts.push(`<style id="uqeb-official-letter-css">${officialCss}</style>`);
  parts.push('</head>', `<body>${safeBody}</body>`, '</html>');

  return parts.join('');
}
