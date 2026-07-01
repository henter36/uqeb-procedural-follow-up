/** عرض التاريخ الميلادي والهجري (أم القرى) — الإدخال الهجري مرحلة لاحقة */
export function formatGregorian(date: string | Date): string {
  const d = typeof date === 'string' ? new Date(date) : date;
  return d.toLocaleDateString('ar-SA', { year: 'numeric', month: 'long', day: 'numeric' });
}

function toDisplayDate(date: string | Date): Date {
  if (date instanceof Date) return date;

  const dateOnlyMatch = /^(\\d{4})-(\\d{2})-(\\d{2})$/.exec(date);
  if (dateOnlyMatch) {
    const [, year, month, day] = dateOnlyMatch;
    return new Date(Number(year), Number(month) - 1, Number(day), 12, 0, 0);
  }

  return new Date(date);
}

export function formatHijri(date: string | Date): string {
  const d = toDisplayDate(date);
  if (Number.isNaN(d.getTime())) return '-';

  const formatWithLocale = (locale: string): string => {
    const parts = Object.fromEntries(
      new Intl.DateTimeFormat(locale, {
        year: 'numeric',
        month: 'numeric',
        day: 'numeric',
      })
        .formatToParts(d)
        .map((p) => [p.type, p.value]),
    );

    const day = parts.day;
    const month = parts.month;
    const year = parts.year;

    if (!day || !month || !year) {
      throw new Error('Missing Hijri date parts');
    }

    return `${day}/${month}/${year}`;
  };

  try {
    return formatWithLocale('ar-SA-u-ca-islamic-umalqura');
  } catch {
    try {
      return formatWithLocale('ar-SA-u-ca-islamic');
    } catch {
      return '-';
    }
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
