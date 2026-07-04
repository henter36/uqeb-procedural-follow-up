import { describe, expect, it } from 'vitest';
import {
  buildMonthlyPeriodKey, buildQuarterlyPeriodKey, buildSemiAnnualPeriodKey, buildAnnualPeriodKey,
  getPeriodLabel, getExpectedDueDate,
} from './recurringPeriod';

describe('recurringPeriod', () => {
  it('builds period keys for each recurrence type', () => {
    expect(buildMonthlyPeriodKey('2026-07')).toBe('2026-07');
    expect(buildQuarterlyPeriodKey(2026, 3)).toBe('2026-Q3');
    expect(buildSemiAnnualPeriodKey(2026, 1)).toBe('2026-H1');
    expect(buildAnnualPeriodKey(2026)).toBe('2026');
  });

  it('labels each recurrence type in Arabic', () => {
    expect(getPeriodLabel('Monthly', '2026-07')).toBe('يوليو 2026');
    expect(getPeriodLabel('Quarterly', '2026-Q3')).toBe('الربع الثالث 2026');
    expect(getPeriodLabel('SemiAnnual', '2026-H1')).toBe('النصف الأول 2026');
    expect(getPeriodLabel('Annual', '2026')).toBe('سنة 2026');
  });

  it('falls back to the raw period key for malformed input', () => {
    expect(getPeriodLabel('SemiAnnual', 'garbage')).toBe('garbage');
    expect(getPeriodLabel('Annual', 'garbage')).toBe('garbage');
  });

  it('computes the expected due date for SemiAnnual and Annual periods', () => {
    expect(getExpectedDueDate('Monthly', '2026-07', '2026-07-10')).toBe('2026-08-10');
    expect(getExpectedDueDate('Quarterly', '2026-Q3', '2026-07-10')).toBe('2026-10-10');
    expect(getExpectedDueDate('SemiAnnual', '2026-H2', '2026-07-10')).toBe('2027-01-10');
    expect(getExpectedDueDate('Annual', '2026', '2026-07-10')).toBe('2027-07-10');
  });

  it('returns null when the period key does not match the recurrence type', () => {
    expect(getExpectedDueDate('SemiAnnual', 'not-a-period', '2026-07-10')).toBeNull();
    expect(getExpectedDueDate('Annual', 'not-a-period', '2026-07-10')).toBeNull();
  });
});
