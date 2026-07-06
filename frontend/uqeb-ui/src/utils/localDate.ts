/** Today's date as YYYY-MM-DD using local calendar parts (not UTC). */
export function todayLocalIso(date = new Date()): string {
  const year = date.getFullYear();
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${year}-${month}-${day}`;
}

/** Add calendar days to a YYYY-MM-DD string using local date parts (no UTC drift). */
export function addDaysIso(baseDate: string, days: number): string {
  const [year, month, day] = baseDate.split('-').map(Number);
  const date = new Date(year, month - 1, day);
  date.setDate(date.getDate() + days);

  const resultYear = date.getFullYear();
  const resultMonth = String(date.getMonth() + 1).padStart(2, '0');
  const resultDay = String(date.getDate()).padStart(2, '0');

  return `${resultYear}-${resultMonth}-${resultDay}`;
}

/** Difference in calendar days between two YYYY-MM-DD strings. */
export function diffDaysIso(startDate: string, endDate: string): number {
  const [startYear, startMonth, startDay] = startDate.split('-').map(Number);
  const [endYear, endMonth, endDay] = endDate.split('-').map(Number);
  const start = Date.UTC(startYear, startMonth - 1, startDay);
  const end = Date.UTC(endYear, endMonth - 1, endDay);
  return Math.round((end - start) / 86400000);
}

export const FUTURE_EVENT_DATE_MESSAGE = 'لا يمكن أن يكون التاريخ بعد تاريخ اليوم.';

export function isFutureLocalDate(value: string): boolean {
  return Boolean(value && value > todayLocalIso());
}
