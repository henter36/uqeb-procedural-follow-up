export function openHtmlPrintWindow(html: string): void {
  const blob = new Blob([html], { type: 'text/html;charset=utf-8' });
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
