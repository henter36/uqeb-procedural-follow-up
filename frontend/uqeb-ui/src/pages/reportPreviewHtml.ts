/** Reject stylesheet payloads that could break out of the embedded <style> block. */
export function assertSafeStylesheet(stylesheet: string): void {
  const normalizedStylesheet = stylesheet.toLowerCase();

  if (normalizedStylesheet.includes('</style')) {
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
