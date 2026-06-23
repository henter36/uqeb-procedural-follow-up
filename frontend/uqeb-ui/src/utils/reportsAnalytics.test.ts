import { describe, expect, it } from 'vitest';
import { getAnalyticsStatusText, getAnalyticsViewState } from './reportsAnalytics';

describe('reportsAnalytics helpers', () => {
  it('returns loading text while refreshing', () => {
    expect(getAnalyticsStatusText(true, null, true)).toBe('جاري تحديث التحليلات...');
  });

  it('returns updated timestamp when available', () => {
    const updatedAt = new Date('2026-01-01T12:00:00');
    expect(getAnalyticsStatusText(false, updatedAt, true)).toContain('آخر تحديث:');
  });

  it('maps view states without nested ternary', () => {
    expect(getAnalyticsViewState(true, false)).toBe('loading-initial');
    expect(getAnalyticsViewState(true, true)).toBe('loading-refresh');
    expect(getAnalyticsViewState(false, true)).toBe('content');
    expect(getAnalyticsViewState(false, false)).toBe('empty');
  });
});
