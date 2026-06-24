/** Reject stylesheet payloads that could break out of the embedded <style> block. */
export function assertSafeStylesheet(stylesheet: string): void {
  if (/<\s*\/?\s*style/i.test(stylesheet) || /<\/style/i.test(stylesheet)) {
    throw new Error('Unsafe stylesheet content.');
  }
}

export function buildPreviewDocument(stylesheet: string, sanitizedHtml: string): string {
  assertSafeStylesheet(stylesheet);
  return `<!doctype html>
<html lang="ar" dir="rtl">
<head>
  <meta charset="utf-8" />
  <style>${stylesheet}</style>
</head>
<body>
  ${sanitizedHtml}
</body>
</html>`;
}
