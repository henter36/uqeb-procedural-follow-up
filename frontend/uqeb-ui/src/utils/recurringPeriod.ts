const ARABIC_MONTH_NAMES = [
  'يناير', 'فبراير', 'مارس', 'أبريل', 'مايو', 'يونيو',
  'يوليو', 'أغسطس', 'سبتمبر', 'أكتوبر', 'نوفمبر', 'ديسمبر',
];

const ARABIC_QUARTER_ORDINALS = ['الأول', 'الثاني', 'الثالث', 'الرابع'];

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

export function buildMonthlyPeriodKey(monthInputValue: string): string {
  return monthInputValue;
}

export function buildQuarterlyPeriodKey(year: number, quarter: number): string {
  return `${year}-Q${quarter}`;
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
  return periodKey;
}

function lastDayOfMonth(year: number, month: number): Date {
  return new Date(Date.UTC(year, month, 0));
}

export function getExpectedDueDate(
  recurrenceType: string,
  periodKey: string,
  dueDaysAfterPeriodEnd: number,
): string | null {
  let periodEnd: Date | null = null;

  if (recurrenceType === 'Monthly') {
    const parsed = parseYearMonth(periodKey);
    if (parsed) periodEnd = lastDayOfMonth(parsed.year, parsed.month);
  } else if (recurrenceType === 'Quarterly') {
    const parsed = parseYearQuarter(periodKey);
    if (parsed) {
      const endMonth = parsed.quarter * 3;
      periodEnd = lastDayOfMonth(parsed.year, endMonth);
    }
  }

  if (!periodEnd) return null;

  const dueDate = new Date(periodEnd);
  dueDate.setUTCDate(dueDate.getUTCDate() + dueDaysAfterPeriodEnd);
  return dueDate.toISOString().split('T')[0];
}
