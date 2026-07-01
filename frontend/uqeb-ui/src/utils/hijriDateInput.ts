export type HijriDateParts = Readonly<{
  year: number;
  month: number;
  day: number;
}>;

const arabicDigits: Record<string, string> = {
  '٠': '0',
  '١': '1',
  '٢': '2',
  '٣': '3',
  '٤': '4',
  '٥': '5',
  '٦': '6',
  '٧': '7',
  '٨': '8',
  '٩': '9',
  '۰': '0',
  '۱': '1',
  '۲': '2',
  '۳': '3',
  '۴': '4',
  '۵': '5',
  '۶': '6',
  '۷': '7',
  '۸': '8',
  '۹': '9',
};

const hijriFormatter = new Intl.DateTimeFormat('en-u-ca-islamic-umalqura', {
  year: 'numeric',
  month: 'numeric',
  day: 'numeric',
});

function pad2(value: number): string {
  return String(value).padStart(2, '0');
}

function toLocalGregorianDate(value: string): Date {
  const [year, month, day] = value.split('-').map(Number);
  return new Date(year, month - 1, day, 12, 0, 0);
}

export function normalizeHijriDigits(value: string): string {
  return Array.from(value).map((char) => arabicDigits[char] ?? char).join('');
}

export function parseHijriInput(value: string): HijriDateParts | null {
  const normalized = normalizeHijriDigits(value).trim().replaceAll('-', '/');
  const parts = normalized.split('/').map((part) => part.trim());
  if (parts.length !== 3) return null;

  const [yearText, monthText, dayText] = parts;
  if (!/^\d+$/.test(yearText) || !/^\d+$/.test(monthText) || !/^\d+$/.test(dayText)) return null;

  const year = Number(yearText);
  const month = Number(monthText);
  const day = Number(dayText);

  if (year < 1300 || year > 1700 || month < 1 || month > 12 || day < 1 || day > 30) return null;
  return { year, month, day };
}

export function formatHijriInputParts(parts: HijriDateParts): string {
  return `${parts.year}/${pad2(parts.month)}/${pad2(parts.day)}`;
}

export function gregorianToHijriParts(value: string | Date): HijriDateParts | null {
  const date = value instanceof Date ? value : toLocalGregorianDate(value.split('T')[0]);
  if (Number.isNaN(date.getTime())) return null;

  const parts = Object.fromEntries(
    hijriFormatter.formatToParts(date).map((part) => [part.type, part.value]),
  );
  const year = Number(parts.year);
  const month = Number(parts.month);
  const day = Number(parts.day);

  return Number.isFinite(year) && Number.isFinite(month) && Number.isFinite(day)
    ? { year, month, day }
    : null;
}

function toGregorianIso(date: Date): string {
  return `${date.getFullYear()}-${pad2(date.getMonth() + 1)}-${pad2(date.getDate())}`;
}

function hijriPartsEqual(left: HijriDateParts | null, right: HijriDateParts): boolean {
  return Boolean(left && left.year === right.year && left.month === right.month && left.day === right.day);
}

export function hijriToGregorianDateString(parts: HijriDateParts): string | null {
  const start = new Date(parts.year + 578, 0, 1, 12, 0, 0);
  for (let offset = 0; offset < 800; offset += 1) {
    const candidate = new Date(start);
    candidate.setDate(start.getDate() + offset);
    if (hijriPartsEqual(gregorianToHijriParts(candidate), parts)) {
      return toGregorianIso(candidate);
    }
  }

  return null;
}
