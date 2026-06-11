/** عرض التاريخ الميلادي والهجري (أم القرى) — الإدخال الهجري مرحلة لاحقة */
export function formatGregorian(date: string | Date): string {
  const d = typeof date === 'string' ? new Date(date) : date;
  return d.toLocaleDateString('ar-SA', { year: 'numeric', month: 'long', day: 'numeric' });
}

export function formatHijri(date: string | Date): string {
  const d = typeof date === 'string' ? new Date(date) : date;
  try {
    return new Intl.DateTimeFormat('ar-SA-u-ca-islamic-umalqura', {
      year: 'numeric', month: 'long', day: 'numeric',
    }).format(d);
  } catch {
    return new Intl.DateTimeFormat('ar-SA-u-ca-islamic', {
      year: 'numeric', month: 'long', day: 'numeric',
    }).format(d);
  }
}

export function formatDualDate(date: string | Date): string {
  return `${formatGregorian(date)} (${formatHijri(date)})`;
}
