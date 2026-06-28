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

const hijriNumericLocales = ['en-u-ca-islamic-umalqura', 'ar-SA-u-ca-islamic'] as const;

function formatHijriNumericWithLocale(d: Date, locale: string): string {
  const fmt = new Intl.DateTimeFormat(locale, {
    year: 'numeric', month: 'numeric', day: 'numeric',
  });
  const parts = Object.fromEntries(fmt.formatToParts(d).map((p) => [p.type, p.value]));
  const y = parts.year ?? '';
  const m = String(parts.month ?? '').padStart(2, '0');
  const dd = String(parts.day ?? '').padStart(2, '0');
  return `${y}/${m}/${dd}`;
}

export function formatHijriNumeric(value: string | Date | null | undefined): string {
  if (!value) return '';
  const d = value instanceof Date ? value : new Date(value);
  if (Number.isNaN(d.getTime())) return '';
  for (const locale of hijriNumericLocales) {
    try {
      return formatHijriNumericWithLocale(d, locale);
    } catch {
      // try next locale
    }
  }
  return '';
}
