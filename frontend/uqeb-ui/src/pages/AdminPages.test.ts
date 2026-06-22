import { describe, expect, it } from 'vitest';
import { normalizeName } from '../pages/AdminPages';

describe('normalizeName', () => {
  it('trims and collapses spaces', () => {
    expect(normalizeName('  IT   Dept ')).toBe('it dept');
  });

  it('is case insensitive for latin', () => {
    expect(normalizeName('Finance')).toBe(normalizeName('finance'));
  });
});
