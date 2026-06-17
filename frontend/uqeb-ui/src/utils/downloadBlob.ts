/** Trigger a browser file download from a Blob without navigation or a new window. */
export function downloadBlob(data: Blob, filename: string): void {
  const url = window.URL.createObjectURL(data);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = filename;
  anchor.style.display = 'none';
  document.body.appendChild(anchor);
  anchor.click();
  document.body.removeChild(anchor);
  window.URL.revokeObjectURL(url);
}

/** Prefer server-provided filename from Content-Disposition when present. */
export function resolveDownloadFilename(
  contentDisposition: string | undefined,
  fallback: string,
): string {
  if (!contentDisposition) return fallback;

  const utf8Match = /filename\*=UTF-8''([^;]+)/i.exec(contentDisposition);
  if (utf8Match?.[1]) {
    try {
      return decodeURIComponent(utf8Match[1].trim());
    } catch {
      return fallback;
    }
  }

  const plainMatch = /filename="?([^";]+)"?/i.exec(contentDisposition);
  return plainMatch?.[1]?.trim() || fallback;
}
