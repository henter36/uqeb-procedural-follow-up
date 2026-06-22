import { describe, expect, it } from 'vitest';
import { isLegacyDetailsTab, parseDetailTab } from './transactionDetailTabs';

describe('parseDetailTab', () => {
  it('defaults to details tab', () => {
    expect(parseDetailTab(null)).toBe('details');
    expect(parseDetailTab('')).toBe('details');
  });

  it('maps legacy tabs to details', () => {
    expect(parseDetailTab('overview')).toBe('details');
    expect(parseDetailTab('assignments')).toBe('details');
    expect(parseDetailTab('followups')).toBe('details');
    expect(parseDetailTab('attachments')).toBe('details');
  });

  it('keeps timeline and audit tabs', () => {
    expect(parseDetailTab('timeline')).toBe('timeline');
    expect(parseDetailTab('audit')).toBe('audit');
  });

  it('identifies legacy detail tab query values', () => {
    expect(isLegacyDetailsTab('overview')).toBe(true);
    expect(isLegacyDetailsTab('timeline')).toBe(false);
  });
});
