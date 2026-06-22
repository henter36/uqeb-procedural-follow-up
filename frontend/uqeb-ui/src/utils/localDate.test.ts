import { describe, expect, it } from 'vitest';
import { addDaysIso, todayLocalIso } from './localDate';

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

describe('todayLocalIso', () => {
  it('returns YYYY-MM-DD format', () => {
    expect(todayLocalIso(new Date(2026, 5, 22))).toBe('2026-06-22');
    expect(todayLocalIso(new Date(2026, 0, 5))).toBe('2026-01-05');
  });

  it('uses local date parts instead of UTC', () => {
    const utcMidnight = new Date('2026-06-22T00:30:00.000Z');
    const localParts = todayLocalIso(utcMidnight);
    const utcParts = utcMidnight.toISOString().split('T')[0];
    expect(localParts).toMatch(/^\d{4}-\d{2}-\d{2}$/);
    expect(localParts).toBe(
      `${utcMidnight.getFullYear()}-${String(utcMidnight.getMonth() + 1).padStart(2, '0')}-${String(utcMidnight.getDate()).padStart(2, '0')}`,
    );
    if (utcMidnight.getTimezoneOffset() < 0 && localParts !== utcParts) {
      expect(localParts).not.toBe(utcParts);
    }
  });

  it('returns the correct local calendar day in a positive-offset timezone', () => {
    const originalTz = process.env.TZ;
    process.env.TZ = 'Asia/Riyadh';
    try {
      const earlyMorningUtc = new Date('2026-06-22T21:30:00.000Z');
      expect(todayLocalIso(earlyMorningUtc)).toBe('2026-06-23');
      expect(todayLocalIso(earlyMorningUtc)).not.toBe(earlyMorningUtc.toISOString().split('T')[0]);
    } finally {
      process.env.TZ = originalTz;
    }
  });
});
