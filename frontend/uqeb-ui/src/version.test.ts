import { describe, expect, it } from 'vitest';
import { normalizeCommitSha } from './version';

describe('normalizeCommitSha', () => {
  it.each([
    ['abcdef1234567890abcdef1234567890abcdef12', 'abcdef1'],
    ['abc1234', 'abc1234'],
    ['ABCDEF1234567890', 'abcdef1'],
    [' local ', 'local'],
    ['not-a-sha', 'local'],
    ['', 'local'],
    [undefined, 'local'],
  ])('normalizes %s to %s', (value, expected) => {
    expect(normalizeCommitSha(value)).toBe(expected);
  });
});
