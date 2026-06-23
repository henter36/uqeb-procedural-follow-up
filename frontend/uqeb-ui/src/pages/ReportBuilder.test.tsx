import { describe, expect, it } from 'vitest';
import { buildReportExportPageSelection } from './ReportBuilder';

describe('buildReportExportPageSelection', () => {
  it('sends page range only when range mode is active', () => {
    const payload = buildReportExportPageSelection(3, 'range', [1, 2], '3-5', 1);
    expect(payload).toEqual({
      selectedPageNumbers: [],
      pageRangeExpression: '3-5',
      currentPageNumber: null,
    });
  });

  it('sends selected thumbnails only when thumbnail mode is active', () => {
    const payload = buildReportExportPageSelection(3, 'thumbnails', [1, 4], '', 2);
    expect(payload).toEqual({
      selectedPageNumbers: [1, 4],
      pageRangeExpression: null,
      currentPageNumber: null,
    });
  });

  it('prefers range when both values exist but range mode is selected', () => {
    const payload = buildReportExportPageSelection(3, 'range', [2], '1,2', 5);
    expect(payload.pageRangeExpression).toBe('1,2');
    expect(payload.selectedPageNumbers).toEqual([]);
  });

  it('uses null instead of empty strings for cleared dates', () => {
    const dateFrom = '';
    const dateTo = '';
    const filters = {
      dateFrom: dateFrom || null,
      dateTo: dateTo || null,
    };
    expect(filters.dateFrom).toBeNull();
    expect(filters.dateTo).toBeNull();
  });
});
