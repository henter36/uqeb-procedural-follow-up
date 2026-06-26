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

/**
 * Sanitizes a full HTML document returned by the server.
 * Preserves <style> in <head> (server-generated CSS), removes <script>
 * elements and event-handler attributes, then returns the full document.
 * Use this for trusted server-generated print views to keep the formal layout.
 */
export function sanitizeFullDocumentHtml(html: string): string {
  if (!html) return '';

  const doc = new DOMParser().parseFromString(html, 'text/html');

  // Remove all <script> elements
  doc.querySelectorAll('script').forEach((el) => el.remove());

  // Remove all event-handler attributes and dangerous inline attributes
  doc.querySelectorAll('*').forEach((el) => {
    const toRemove: string[] = [];
    for (const attr of el.attributes) {
      if (
        attr.name.startsWith('on')
        || attr.name === 'srcdoc'
      ) {
        toRemove.push(attr.name);
      }
    }
    toRemove.forEach((name) => el.removeAttribute(name));

    // Strip javascript: hrefs/srcs
    for (const attrName of ['href', 'src', 'action']) {
      const val = el.getAttribute(attrName) ?? '';
      if (/^javascript:/i.test(val)) el.removeAttribute(attrName);
    }
  });

  // Remove <iframe>, <object>, <embed>, <form> inside body (not our letter layout)
  doc.body.querySelectorAll('iframe, object, embed, form').forEach((el) => el.remove());

  return doc.documentElement.outerHTML;
}
