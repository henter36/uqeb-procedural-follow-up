const ARABIC_MONTH_NAMES = [
  'يناير', 'فبراير', 'مارس', 'أبريل', 'مايو', 'يونيو',
  'يوليو', 'أغسطس', 'سبتمبر', 'أكتوبر', 'نوفمبر', 'ديسمبر',
];

const ARABIC_QUARTER_ORDINALS = ['الأول', 'الثاني', 'الثالث', 'الرابع'];
const ARABIC_HALF_ORDINALS = ['الأول', 'الثاني'];

function parseYearMonth(periodKey: string): { year: number; month: number } | null {
  const match = /^(\d{4})-(\d{2})$/.exec(periodKey);
  if (!match) return null;
  const year = Number(match[1]);
  const month = Number(match[2]);
  if (month < 1 || month > 12) return null;
  return { year, month };
}

function parseYearQuarter(periodKey: string): { year: number; quarter: number } | null {
  const match = /^(\d{4})-[Qq]([1-4])$/.exec(periodKey);
  if (!match) return null;
  return { year: Number(match[1]), quarter: Number(match[2]) };
}

function parseYearHalf(periodKey: string): { year: number; half: number } | null {
  const match = /^(\d{4})-[Hh]([1-2])$/.exec(periodKey);
  if (!match) return null;
  return { year: Number(match[1]), half: Number(match[2]) };
}

function parseYearOnly(periodKey: string): { year: number } | null {
  const match = /^(\d{4})$/.exec(periodKey);
  if (!match) return null;
  return { year: Number(match[1]) };
}

function daysInUtcMonth(year: number, monthIndex: number): number {
  return new Date(Date.UTC(year, monthIndex + 1, 0)).getUTCDate();
}

function addUtcMonthsClamped(date: Date, months: number): Date {
  const targetMonthIndex = date.getUTCMonth() + months;
  const targetYear = date.getUTCFullYear() + Math.floor(targetMonthIndex / 12);
  const normalizedMonthIndex = ((targetMonthIndex % 12) + 12) % 12;
  const day = Math.min(date.getUTCDate(), daysInUtcMonth(targetYear, normalizedMonthIndex));
  return new Date(Date.UTC(targetYear, normalizedMonthIndex, day));
}

export function buildMonthlyPeriodKey(monthInputValue: string): string {
  return monthInputValue;
}

export function buildQuarterlyPeriodKey(year: number, quarter: number): string {
  return `${year}-Q${quarter}`;
}

export function buildSemiAnnualPeriodKey(year: number, half: number): string {
  return `${year}-H${half}`;
}

export function buildAnnualPeriodKey(year: number): string {
  return `${year}`;
}

export function getPeriodLabel(recurrenceType: string, periodKey: string): string {
  if (recurrenceType === 'Monthly') {
    const parsed = parseYearMonth(periodKey);
    if (!parsed) return periodKey;
    return `${ARABIC_MONTH_NAMES[parsed.month - 1]} ${parsed.year}`;
  }
  if (recurrenceType === 'Quarterly') {
    const parsed = parseYearQuarter(periodKey);
    if (!parsed) return periodKey;
    return `الربع ${ARABIC_QUARTER_ORDINALS[parsed.quarter - 1]} ${parsed.year}`;
  }
  if (recurrenceType === 'SemiAnnual') {
    const parsed = parseYearHalf(periodKey);
    if (!parsed) return periodKey;
    return `النصف ${ARABIC_HALF_ORDINALS[parsed.half - 1]} ${parsed.year}`;
  }
  if (recurrenceType === 'Annual') {
    const parsed = parseYearOnly(periodKey);
    if (!parsed) return periodKey;
    return `سنة ${parsed.year}`;
  }
  return periodKey;
}

export function getExpectedDueDate(
  recurrenceType: string,
  periodKey: string,
  startDate: string,
): string | null {
  if (!startDate) return null;

  const anchorDate = new Date(`${startDate.split('T')[0]}T00:00:00Z`);
  if (Number.isNaN(anchorDate.getTime())) return null;

  let targetYear: number | null = null;
  let targetStartMonth: number | null = null;
  let spanMonths: number | null = null;

  if (recurrenceType === 'Monthly') {
    const parsed = parseYearMonth(periodKey);
    if (parsed) {
      targetYear = parsed.year;
      targetStartMonth = parsed.month;
      spanMonths = 1;
    }
  } else if (recurrenceType === 'Quarterly') {
    const parsed = parseYearQuarter(periodKey);
    if (parsed) {
      targetYear = parsed.year;
      targetStartMonth = ((parsed.quarter - 1) * 3) + 1;
      spanMonths = 3;
    }
  } else if (recurrenceType === 'SemiAnnual') {
    const parsed = parseYearHalf(periodKey);
    if (parsed) {
      targetYear = parsed.year;
      targetStartMonth = ((parsed.half - 1) * 6) + 1;
      spanMonths = 6;
    }
  } else if (recurrenceType === 'Annual') {
    const parsed = parseYearOnly(periodKey);
    if (parsed) {
      targetYear = parsed.year;
      targetStartMonth = 1;
      spanMonths = 12;
    }
  }

  if (targetYear === null || targetStartMonth === null || spanMonths === null) return null;

  const anchorPeriodStartMonth = (Math.floor(anchorDate.getUTCMonth() / spanMonths) * spanMonths) + 1;
  const monthOffset = ((targetYear - anchorDate.getUTCFullYear()) * 12) + targetStartMonth - anchorPeriodStartMonth;
  const periodIndex = Math.trunc(monthOffset / spanMonths);
  const dueDate = addUtcMonthsClamped(anchorDate, (periodIndex + 1) * spanMonths);
  return dueDate.toISOString().split('T')[0];
}
