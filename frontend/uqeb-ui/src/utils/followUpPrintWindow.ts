export function openHtmlPrintWindow(html: string): void {
  const safeHtml = sanitizePrintHtml(html);
  const blob = new Blob([safeHtml], { type: 'text/html;charset=utf-8' });
  const url = URL.createObjectURL(blob);
  const win = globalThis.open(url, '_blank', 'noopener,noreferrer');
  if (!win) {
    URL.revokeObjectURL(url);
    throw new Error('تعذر فتح نافذة الطباعة. تحقق من إعدادات النوافذ المنبثقة.');
  }

  win.addEventListener('load', () => {
    URL.revokeObjectURL(url);
  }, { once: true });
  win.focus();
}

function sanitizePrintHtml(html: string): string {
  const parser = new DOMParser();
  const document = parser.parseFromString(html, 'text/html');

  document.querySelectorAll('script, iframe, object, embed').forEach((node) => {
    node.remove();
  });

  document.querySelectorAll('*').forEach((element) => {
    [...element.attributes].forEach((attribute) => {
      const name = attribute.name.toLowerCase();
      const value = attribute.value.trim().toLowerCase();
      const isUrlAttribute = name === 'href' || name === 'src' || name === 'xlink:href';

      if (
        name.startsWith('on') ||
        name === 'srcdoc' ||
        (isUrlAttribute && (value.startsWith('javascript:') || value.startsWith('data:text/html')))
      ) {
        element.removeAttribute(attribute.name);
      }
    });
  });

  return `<!doctype html>\n${document.documentElement.outerHTML}`;
}
