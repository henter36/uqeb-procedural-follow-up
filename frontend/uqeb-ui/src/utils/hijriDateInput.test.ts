import { describe, expect, it } from 'vitest';
import {
  gregorianToHijriParts,
  hijriToGregorianDateString,
  normalizeHijriDigits,
  parseHijriInput,
} from './hijriDateInput';

describe('hijriDateInput', () => {
  it('converts a stored Gregorian date to Hijri parts', () => {
    expect(gregorianToHijriParts('2026-07-01')).toEqual({ year: 1448, month: 1, day: 16 });
  });

  it('converts Hijri input to Gregorian ISO date', () => {
    expect(hijriToGregorianDateString({ year: 1448, month: 1, day: 16 })).toBe('2026-07-01');
  });

  it('accepts Arabic and Persian digits', () => {
    expect(normalizeHijriDigits('١٤٤٨/۰۱/۱۰')).toBe('1448/01/10');
    expect(parseHijriInput('١٤٤٨/٠١/١٦')).toEqual({ year: 1448, month: 1, day: 16 });
  });

  it('rejects invalid Hijri dates', () => {
    expect(parseHijriInput('1448/13/01')).toBeNull();
    expect(hijriToGregorianDateString({ year: 1448, month: 2, day: 31 })).toBeNull();
  });
});
