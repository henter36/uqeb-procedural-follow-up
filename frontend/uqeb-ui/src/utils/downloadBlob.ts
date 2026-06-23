/**
 * Trigger a browser download for a Blob and always revoke the object URL.
 */
export function downloadBlob(blob: Blob, fileName: string): void {
  const url = globalThis.URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = fileName;
  document.body.appendChild(anchor);
  try {
    anchor.click();
  } finally {
    anchor.remove();
    globalThis.URL.revokeObjectURL(url);
  }
}
