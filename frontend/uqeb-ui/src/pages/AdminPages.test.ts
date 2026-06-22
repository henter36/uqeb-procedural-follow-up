import { describe, expect, it } from 'vitest';
import { isEmptyTrimmedName, normalizeName, normalizeTrimmedName } from './adminPageHelpers';

describe('normalizeName', () => {
  it('trims and collapses spaces', () => {
    expect(normalizeName('  IT   Dept ')).toBe('it dept');
  });

  it('is case insensitive for latin', () => {
    expect(normalizeName('Finance')).toBe(normalizeName('finance'));
  });
});

describe('normalizeTrimmedName', () => {
  it('treats whitespace-only names as empty', () => {
    expect(isEmptyTrimmedName('   ')).toBe(true);
    expect(isEmptyTrimmedName('  \t  ')).toBe(true);
    expect(normalizeTrimmedName('   ')).toBe('');
  });

  it('keeps meaningful names after normalization', () => {
    expect(isEmptyTrimmedName('  Finance  ')).toBe(false);
    expect(normalizeTrimmedName('  Finance  ')).toBe('Finance');
  });
});
