import { describe, expect, it } from 'vitest';
import { addDaysIso } from './localDate';

describe('addDaysIso', () => {
  it('adds one day within the same month', () => {
    expect(addDaysIso('2026-01-15', 1)).toBe('2026-01-16');
  });

  it('rolls over month end', () => {
    expect(addDaysIso('2026-01-31', 1)).toBe('2026-02-01');
  });

  it('rolls over year end', () => {
    expect(addDaysIso('2025-12-31', 1)).toBe('2026-01-01');
  });

  it('uses local date parts without UTC drift', () => {
    const originalTz = process.env.TZ;
    process.env.TZ = 'Pacific/Kiritimati';
    try {
      expect(addDaysIso('2026-03-01', 0)).toBe('2026-03-01');
      expect(addDaysIso('2026-03-01', 2)).toBe('2026-03-03');
    } finally {
      process.env.TZ = originalTz;
    }
  });
});
