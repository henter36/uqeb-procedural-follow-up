import { describe, expect, it } from 'vitest';
import { formatCompletionDays } from './responseTiming';

describe('formatCompletionDays', () => {
  it('formats empty and Arabic day counts', () => {
    expect(formatCompletionDays(null)).toBe('—');
    expect(formatCompletionDays(undefined)).toBe('—');
    expect(formatCompletionDays(0)).toBe('نفس اليوم');
    expect(formatCompletionDays(1)).toBe('يوم واحد');
    expect(formatCompletionDays(2)).toBe('يومان');
    expect(formatCompletionDays(3)).toBe('3 أيام');
    expect(formatCompletionDays(10)).toBe('10 أيام');
    expect(formatCompletionDays(11)).toBe('11 يوم');
  });
});
