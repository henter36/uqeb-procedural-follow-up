import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import ReportBuilderPage, { buildReportExportPageSelection, defaultDate } from './ReportBuilder';
import {
  getPageSelectionSummary,
  getPreviewStatusMessage,
  resolveEffectiveOverflowAction,
  resolveExportFileExtension,
} from './reportBuilderHelpers';
import { ExportMode, ExportFormat, DetailOverflowAction } from '../api/institutionalReports.constants';
import * as services from '../api/services';

vi.mock('../api/services', () => ({
  institutionalReportsApi: {
    preview: vi.fn(),
    export: vi.fn(),
  },
}));

const mockManifest = {
  reportId: 'test-report',
  totalPages: 2,
  pages: [
    { originalPageNumber: 1, sectionName: 'الغلاف', htmlContent: '<p>صفحة 1</p>' },
    { originalPageNumber: 2, sectionName: 'الملخص', htmlContent: '<p>صفحة 2</p>' },
  ],
};

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

describe('reportBuilderHelpers', () => {
  it('getPreviewStatusMessage returns empty state when no manifest', () => {
    expect(getPreviewStatusMessage(false, '', false)).toBe('اضغط «معاينة التقرير» لعرض الصفحات.');
  });

  it('getPageSelectionSummary summarizes thumbnail selection', () => {
    expect(getPageSelectionSummary('thumbnails', [1, 2], '', 5)).toBe('تم تحديد 2 من 5 صفحات (تحديد مصغرات)');
  });

  it('resolveEffectiveOverflowAction uses user choice when required', () => {
    expect(resolveEffectiveOverflowAction(true, DetailOverflowAction.SplitPdf, ExportFormat.Pdf, null))
      .toBe(DetailOverflowAction.SplitPdf);
  });

  it('resolveExportFileExtension maps zip content type', () => {
    expect(resolveExportFileExtension('application/zip', ExportFormat.Pdf)).toBe('zip');
  });
});

describe('ReportBuilderPage export dialog', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(services.institutionalReportsApi.preview).mockResolvedValue({ data: mockManifest } as never);
  });

  afterEach(() => {
    cleanup();
  });

  it('opens native dialog with accessible title when export is clicked', async () => {
    const user = userEvent.setup();
    render(<ReportBuilderPage />);

    await user.click(screen.getByRole('button', { name: 'معاينة التقرير' }));
    await waitFor(() => {
      expect(screen.getByText('الصفحة 1 من 2')).toBeInTheDocument();
    });

    const exportButton = screen.getByRole('button', { name: 'تصدير' });
    expect(exportButton).not.toBeDisabled();

    await user.click(exportButton);

    const dialog = screen.getByRole('dialog');
    expect(dialog).toBeInTheDocument();
    expect(dialog).toHaveAttribute('aria-labelledby', 'report-export-dialog-title');
    expect(screen.getByRole('heading', { name: 'خيارات تصدير التقرير' })).toHaveAttribute('id', 'report-export-dialog-title');
  });

  it('closes dialog when cancel is clicked', async () => {
    const user = userEvent.setup();
    render(<ReportBuilderPage />);

    await user.click(screen.getByRole('button', { name: 'معاينة التقرير' }));
    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'تصدير' })).not.toBeDisabled();
    });

    await user.click(screen.getByRole('button', { name: 'تصدير' }));
    expect(screen.getByRole('dialog')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'إلغاء' }));

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    });
  });
});
