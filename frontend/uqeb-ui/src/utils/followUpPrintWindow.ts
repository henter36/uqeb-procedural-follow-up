export function openHtmlPrintWindow(html: string): void {
  const win = window.open('', '_blank', 'noopener,noreferrer');
  if (!win) {
    throw new Error('تعذر فتح نافذة الطباعة. تحقق من إعدادات النوافذ المنبثقة.');
  }
  win.document.open();
  win.document.write(html);
  win.document.close();
  win.focus();
}
