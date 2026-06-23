import { describe, expect, it, vi } from 'vitest';
import { buildReportExportPageSelection, defaultDate } from './ReportBuilder';
import { ExportMode } from '../api/institutionalReports.constants';

describe('buildReportExportPageSelection', () => {
  it('sends page range only when range mode is active', () => {
    const payload = buildReportExportPageSelection(ExportMode.SelectedPages, 'range', [1, 2], '3-5', 1);
    expect(payload).toEqual({
      selectedPageNumbers: [],
      pageRangeExpression: '3-5',
      currentPageNumber: null,
    });
  });

  it('sends selected thumbnails only when thumbnail mode is active', () => {
    const payload = buildReportExportPageSelection(ExportMode.SelectedPages, 'thumbnails', [1, 4], '', 2);
    expect(payload).toEqual({
      selectedPageNumbers: [1, 4],
      pageRangeExpression: null,
      currentPageNumber: null,
    });
  });

  it('prefers range when both values exist but range mode is selected', () => {
    const payload = buildReportExportPageSelection(ExportMode.SelectedPages, 'range', [2], '1,2', 5);
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

  it('uses local calendar date for defaults in Asia/Riyadh', () => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-01-01T22:30:00+03:00'));

    expect(defaultDate(0)).toBe('2026-01-01');
    expect(defaultDate(-1)).toBe('2025-12-31');
    expect(defaultDate(1)).toBe('2026-01-02');

    vi.useRealTimers();
  });
});
