import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import axios from 'axios';
import type { AxiosResponse } from 'axios';
import { MemoryRouter } from 'react-router-dom';
import ReportBuilderPage from './ReportBuilder';
import { buildReportExportPageSelection, defaultDate } from './reportBuilderHelpers';
import {
  getPageSelectionSummary,
  getPreviewStatusMessage,
  resolveEffectiveOverflowAction,
  resolveExportFileExtension,
} from './reportBuilderHelpers';
import {
  DetailOverflowAction,
  ExportFormat,
  ExportMode,
  InstitutionalReportType,
  ReportComparisonMode,
  ReportContentLevel,
  ReportSectionId,
  ReportTimeGrouping,
} from '../api/institutionalReports.constants';
import * as services from '../api/services';
import type { InstitutionalReportManifest, ReportTemplate } from '../api/services';
import type { LookupItem } from '../api/types';

function mockAxiosResponse<T>(data: T): AxiosResponse<T> {
  return {
    data,
    status: 200,
    statusText: 'OK',
    headers: {},
    config: { headers: {} } as AxiosResponse<T>['config'],
  };
}

function mockLookupItems(items: Array<Pick<LookupItem, 'id' | 'name'>>): AxiosResponse<LookupItem[]> {
  return mockAxiosResponse(items.map((item) => ({ ...item, isActive: true })));
}

const emptyDeptTransactionsFilters = {
  departmentIds: [] as number[],
  partyIds: [] as number[],
  categoryIds: [] as number[],
  priorities: [] as string[],
  statuses: [] as string[],
  includeOverdue: false,
};

const mockUseAuth = vi.fn(() => ({
  isAdmin: true,
  canClose: true,
  canEdit: true,
  isDepartmentUser: false,
  user: { fullName: 'مختبر', role: 'Admin' },
  logout: vi.fn(),
  login: vi.fn(),
}));

vi.mock('../context/useAuth', () => ({
  useAuth: () => mockUseAuth(),
}));

vi.mock('../api/services', () => ({
  institutionalReportsApi: {
    preview: vi.fn(),
    export: vi.fn(),
    getTemplates: vi.fn().mockResolvedValue({ data: [] }),
    saveTemplate: vi.fn(),
  },
  departmentsApi: {
    lookup: vi.fn().mockResolvedValue({ data: [] }),
  },
  categoriesApi: {
    lookup: vi.fn().mockResolvedValue({ data: [] }),
  },
  externalPartiesApi: {
    lookup: vi.fn().mockResolvedValue({ data: [] }),
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

// mockManifest intentionally omits some InstitutionalReportPage fields (renderedPageNumber,
// sectionId, pageTitle, isSelectable) that this suite's assertions never need - this single,
// well-named helper isolates the resulting type bypass to one place instead of an inline
// `as never` at every preview-mock call site.
function mockPreviewResponse(): AxiosResponse<InstitutionalReportManifest> {
  return { data: mockManifest } as never;
}

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
  let clipboardWriteText: ReturnType<typeof vi.fn>;

  beforeEach(() => {
    vi.clearAllMocks();
    mockUseAuth.mockReturnValue({
      isAdmin: true,
      canClose: true,
      canEdit: true,
      isDepartmentUser: false,
      user: { fullName: 'مختبر', role: 'Admin' },
      logout: vi.fn(),
      login: vi.fn(),
    });
    vi.mocked(services.institutionalReportsApi.preview).mockResolvedValue({ data: mockManifest } as never);
    vi.mocked(services.institutionalReportsApi.getTemplates).mockResolvedValue({ data: [] } as never);
    vi.mocked(services.institutionalReportsApi.saveTemplate).mockReset();
    clipboardWriteText = vi.fn().mockResolvedValue(undefined);
    navigator.clipboard.writeText = clipboardWriteText;
  });

  afterEach(() => {
    cleanup();
  });

  it('keeps dialog closed by default after page render', () => {
    render(<ReportBuilderPage />);

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(document.querySelector('.report-export-modal[open]')).toBeNull();
  });

  it('does not open dialog when preview fails', async () => {
    vi.mocked(services.institutionalReportsApi.preview).mockRejectedValueOnce({
      isAxiosError: true,
      response: {
        status: 500,
        data: {
          message: 'تعذر إنشاء معاينة التقرير.',
          correlationId: 'corr-1',
        },
      },
    });

    const user = userEvent.setup();
    render(<ReportBuilderPage />);

    await user.click(screen.getByRole('button', { name: 'معاينة التقرير' }));

    await waitFor(() => {
      const alert = screen.getByRole('alert');
      expect(alert.querySelector(':scope > div:first-child')).toHaveTextContent('تعذر إنشاء معاينة التقرير.');
    });

    expect(screen.getByRole('alert')).not.toHaveTextContent(/تعذر إنشاء معاينة التقرير: تعذر إنشاء معاينة التقرير/);
    expect(screen.getByText(/رقم التتبع: corr-1/)).toBeInTheDocument();

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'تصدير' })).toBeDisabled();
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
    expect(dialog).toHaveAttribute('open');
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

  it('renders analytical controls and sends analytical preview payload defaults', async () => {
    const user = userEvent.setup();
    render(<ReportBuilderPage />);

    expect(screen.getByLabelText('مستوى المحتوى')).toHaveValue(String(ReportContentLevel.Analytical));
    expect(screen.getByLabelText('نمط المقارنة')).toHaveValue(String(ReportComparisonMode.PreviousEquivalentPeriod));
    expect(screen.getByLabelText('تجميع الاتجاه الزمني')).toHaveValue(String(ReportTimeGrouping.Monthly));
    expect(screen.getByLabelText('الحالات الحرجة')).toBeChecked();
    expect(screen.getByLabelText('جودة البيانات')).toBeChecked();

    await user.click(screen.getByRole('button', { name: 'معاينة التقرير' }));

    await waitFor(() => {
      expect(services.institutionalReportsApi.preview).toHaveBeenCalled();
    });
    const request = vi.mocked(services.institutionalReportsApi.preview).mock.calls[0][0];
    expect(request.contentLevel).toBe(ReportContentLevel.Analytical);
    expect(request.comparisonMode).toBe(ReportComparisonMode.PreviousEquivalentPeriod);
    expect(request.timeGrouping).toBe(ReportTimeGrouping.Monthly);
    expect(request.includeComparison).toBe(true);
    expect(request.includeCriticalCases).toBe(true);
    expect(request.includeDataQuality).toBe(true);
    expect(request.maxFindings).toBe(5);
    expect(request.maxCriticalCases).toBe(10);
    expect(request.maxRecommendations).toBe(10);
    expect(request.sectionIds).toContain(ReportSectionId.KeyPerformanceIndicators);
    expect(request.sectionIds).toContain(ReportSectionId.MethodologyAndDefinitions);
  });

  it('ignores stale preview response when a newer preview starts', async () => {
    let resolveSlow: (value: { data: typeof mockManifest }) => void = () => undefined;
    const slowPromise = new Promise<{ data: typeof mockManifest }>((resolve) => {
      resolveSlow = resolve;
    });
    const freshManifest = {
      ...mockManifest,
      pages: [{ originalPageNumber: 1, sectionName: 'جديد', htmlContent: '<p>fresh</p>' }],
    };

    vi.mocked(services.institutionalReportsApi.preview)
      .mockReturnValueOnce(slowPromise as never)
      .mockResolvedValueOnce({ data: freshManifest } as never);

    const user = userEvent.setup();
    render(<ReportBuilderPage />);

    await user.click(screen.getByRole('button', { name: 'معاينة التقرير' }));
    fireEvent.change(screen.getByLabelText('من تاريخ'), { target: { value: '2026-02-01' } });
    await user.click(screen.getByRole('button', { name: 'معاينة التقرير' }));

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /1\. جديد/ })).toBeInTheDocument();
    });

    resolveSlow({ data: mockManifest });

    await waitFor(() => {
      expect(screen.queryByRole('button', { name: /الغلاف/ })).not.toBeInTheDocument();
      expect(screen.getByRole('button', { name: /1\. جديد/ })).toBeInTheDocument();
    });
  });

  it('closes export dialog when preview is invalidated by filter change', async () => {
    const user = userEvent.setup();
    render(<ReportBuilderPage />);

    await user.click(screen.getByRole('button', { name: 'معاينة التقرير' }));
    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'تصدير' })).not.toBeDisabled();
    });

    await user.click(screen.getByRole('button', { name: 'تصدير' }));
    expect(screen.getByRole('dialog')).toBeInTheDocument();

    await user.clear(screen.getByLabelText('من تاريخ'));
    await user.type(screen.getByLabelText('من تاريخ'), '2026-01-01');

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'تصدير' })).toBeDisabled();
    });
  });

  it('does not reopen export dialog after new preview unless user clicks export', async () => {
    const freshManifest = {
      ...mockManifest,
      reportId: 'fresh-report',
      pages: [{ originalPageNumber: 1, sectionName: 'جديد', htmlContent: '<p>fresh</p>' }],
    };

    vi.mocked(services.institutionalReportsApi.preview)
      .mockResolvedValueOnce({ data: mockManifest } as never)
      .mockResolvedValueOnce({ data: freshManifest } as never);

    const user = userEvent.setup();
    render(<ReportBuilderPage />);

    await user.click(screen.getByRole('button', { name: 'معاينة التقرير' }));
    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'تصدير' })).not.toBeDisabled();
    });

    await user.click(screen.getByRole('button', { name: 'تصدير' }));
    expect(screen.getByRole('dialog')).toBeInTheDocument();

    await user.clear(screen.getByLabelText('من تاريخ'));
    await user.type(screen.getByLabelText('من تاريخ'), '2026-01-01');

    await waitFor(() => {
      expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'تصدير' })).toBeDisabled();
    });

    await user.click(screen.getByRole('button', { name: 'معاينة التقرير' }));
    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'تصدير' })).not.toBeDisabled();
    });

    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'تصدير' }));
    expect(screen.getByRole('dialog')).toBeInTheDocument();
  }, 10000);

  it('shows copied feedback after copying correlation id', async () => {
    const previewError = new axios.AxiosError(
      'Request failed',
      'ERR_BAD_RESPONSE',
      undefined,
      undefined,
      {
        status: 500,
        statusText: 'Internal Server Error',
        headers: {},
        config: { headers: new axios.AxiosHeaders() },
        data: {
          message: 'خطأ خادم',
          correlationId: 'corr-copy-1',
        },
      },
    );
    vi.mocked(services.institutionalReportsApi.preview).mockRejectedValueOnce(previewError);

    const user = userEvent.setup();
    render(<ReportBuilderPage />);

    await user.click(screen.getByRole('button', { name: 'معاينة التقرير' }));
    await waitFor(() => {
      expect(screen.getByText(/رقم التتبع: corr-copy-1/)).toBeInTheDocument();
    });

    await user.click(screen.getByRole('button', { name: 'نسخ رقم التتبع' }));

    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'تم النسخ' })).toBeInTheDocument();
    });
  });

  it('keeps copy button label when clipboard write fails', async () => {
    clipboardWriteText.mockRejectedValue(new Error('denied'));

    const previewError = new axios.AxiosError(
      'Request failed',
      'ERR_BAD_RESPONSE',
      undefined,
      undefined,
      {
        status: 500,
        statusText: 'Internal Server Error',
        headers: {},
        config: { headers: new axios.AxiosHeaders() },
        data: {
          message: 'خطأ خادم',
          correlationId: 'corr-fail-1',
        },
      },
    );
    vi.mocked(services.institutionalReportsApi.preview).mockRejectedValueOnce(previewError);

    const user = userEvent.setup();
    render(<ReportBuilderPage />);

    await user.click(screen.getByRole('button', { name: 'معاينة التقرير' }));
    await waitFor(() => {
      expect(screen.getByText(/رقم التتبع: corr-fail-1/)).toBeInTheDocument();
    });

    await user.click(screen.getByRole('button', { name: 'نسخ رقم التتبع' }));

    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'نسخ رقم التتبع' })).toBeInTheDocument();
    });
  });

  it('redirects non-admin users away from the page', () => {
    mockUseAuth.mockReturnValue({
      isAdmin: false,
      canClose: true,
      canEdit: true,
      isDepartmentUser: false,
      user: { fullName: 'مشرف', role: 'Supervisor' },
      logout: vi.fn(),
      login: vi.fn(),
    });

    const { container } = render(
      <MemoryRouter>
        <ReportBuilderPage />
      </MemoryRouter>,
    );

    expect(container.innerHTML).not.toContain('منشئ التقارير');
    expect(screen.queryByRole('button', { name: 'معاينة التقرير' })).not.toBeInTheDocument();
  });

  it('does not call preview API for non-admin users', async () => {
    mockUseAuth.mockReturnValue({
      isAdmin: false,
      canClose: true,
      canEdit: true,
      isDepartmentUser: false,
      user: { fullName: 'مشرف', role: 'Supervisor' },
      logout: vi.fn(),
      login: vi.fn(),
    });

    render(
      <MemoryRouter>
        <ReportBuilderPage />
      </MemoryRouter>,
    );

    expect(vi.mocked(services.institutionalReportsApi.preview)).not.toHaveBeenCalled();
  });

  it('does not call departmentsApi.lookup for non-admin users', async () => {
    vi.mocked(services.departmentsApi.lookup).mockClear();
    mockUseAuth.mockReturnValue({
      isAdmin: false,
      canClose: true,
      canEdit: true,
      isDepartmentUser: false,
      user: { fullName: 'مشرف', role: 'Supervisor' },
      logout: vi.fn(),
      login: vi.fn(),
    });

    render(
      <MemoryRouter>
        <ReportBuilderPage />
      </MemoryRouter>,
    );

    expect(vi.mocked(services.departmentsApi.lookup)).not.toHaveBeenCalled();
  });

  it('sends includeOverdue: false when overdue filter is off', async () => {
    vi.mocked(services.institutionalReportsApi.preview).mockResolvedValue({ data: mockManifest } as never);
    const user = userEvent.setup();
    render(<ReportBuilderPage />);
    // Default state: overdue filter is off — must not lock to true.
    await user.click(screen.getByRole('button', { name: 'معاينة التقرير' }));
    await waitFor(() => {
      expect(services.institutionalReportsApi.preview).toHaveBeenCalled();
    });
    const request = vi.mocked(services.institutionalReportsApi.preview).mock.calls[0][0];
    expect(request.filters?.includeOverdue).toBe(false);
  });

  it('sends includeOverdue: true when overdue filter is toggled on', async () => {
    vi.mocked(services.institutionalReportsApi.preview).mockResolvedValue({ data: mockManifest } as never);
    const user = userEvent.setup();
    render(<ReportBuilderPage />);
    // Toggle the overdue-only checkbox on.
    const overdueCheckbox = screen.getByRole('checkbox', { name: /متأخرة فقط/i });
    await user.click(overdueCheckbox);
    await user.click(screen.getByRole('button', { name: 'معاينة التقرير' }));
    await waitFor(() => {
      expect(services.institutionalReportsApi.preview).toHaveBeenCalled();
    });
    const request = vi.mocked(services.institutionalReportsApi.preview).mock.calls[0][0];
    expect(request.filters?.includeOverdue).toBe(true);
  });

  it('does not include SingleTransaction in report type options', () => {
    render(<ReportBuilderPage />);
    const select = screen.getByLabelText('نوع التقرير');
    const optionTexts = Array.from(select.querySelectorAll('option')).map((o) => o.textContent);
    expect(optionTexts).not.toContain('تقرير معاملة واحدة');
  });

  it('sends search term in filters when provided', async () => {
    vi.mocked(services.institutionalReportsApi.preview).mockResolvedValue({ data: mockManifest } as never);
    const user = userEvent.setup();
    render(<ReportBuilderPage />);
    await user.type(screen.getByLabelText('بحث'), 'VAR-001');
    await user.click(screen.getByRole('button', { name: 'معاينة التقرير' }));
    await waitFor(() => expect(services.institutionalReportsApi.preview).toHaveBeenCalled());
    const request = vi.mocked(services.institutionalReportsApi.preview).mock.calls[0][0];
    expect(request.filters?.search).toBe('VAR-001');
  });

  it('loads and applies saved template filters to preview payload', async () => {
    vi.mocked(services.institutionalReportsApi.getTemplates).mockResolvedValueOnce({
      data: [
        {
          id: 7,
          name: 'قالب المتأخرة',
          reportType: 2,
          sectionIds: [ReportSectionId.Cover, ReportSectionId.TransactionDetails],
          defaultFilters: {
            dateFrom: '2026-06-01',
            dateTo: '2026-06-10',
            departmentIds: [11],
            partyIds: [22],
            categoryIds: [33],
            priorities: ['Urgent'],
            statuses: ['Overdue'],
            includeOverdue: true,
            search: 'TPL-1',
          },
          defaultFormat: ExportFormat.Xlsx,
          pageNumberingMode: 2,
          includePartialCover: false,
          includePartialManifest: true,
        },
      ],
    } as never);
    vi.mocked(services.institutionalReportsApi.preview).mockResolvedValue({ data: mockManifest } as never);

    const user = userEvent.setup();
    render(<ReportBuilderPage />);

    await waitFor(() => {
      expect(screen.getByRole('option', { name: 'قالب المتأخرة' })).toBeInTheDocument();
    });

    await user.selectOptions(screen.getByLabelText('القالب المحفوظ'), '7');
    await user.click(screen.getByRole('button', { name: 'معاينة التقرير' }));

    await waitFor(() => expect(services.institutionalReportsApi.preview).toHaveBeenCalled());
    const request = vi.mocked(services.institutionalReportsApi.preview).mock.calls[0][0];
    expect(request.reportType).toBe(2);
    expect(request.sectionIds).toEqual([ReportSectionId.Cover, ReportSectionId.TransactionDetails]);
    expect(request.filters.dateFrom).toBe('2026-06-01');
    expect(request.filters.dateTo).toBe('2026-06-10');
    expect(request.filters.departmentIds).toEqual([11]);
    expect(request.filters.partyIds).toEqual([22]);
    expect(request.filters.categoryIds).toEqual([33]);
    expect(request.filters.priorities).toEqual(['Urgent']);
    expect(request.filters.statuses).toEqual(['Overdue']);
    expect(request.filters.includeOverdue).toBe(true);
    expect(request.filters.search).toBe('TPL-1');
  });

  it('applies safe fallbacks when saved template default filters are missing', async () => {
    vi.mocked(services.institutionalReportsApi.getTemplates).mockResolvedValueOnce({
      data: [
        {
          id: 8,
          name: 'قالب ناقص',
          reportType: 1,
          sectionIds: undefined,
          defaultFilters: undefined,
          defaultFormat: ExportFormat.Pdf,
          pageNumberingMode: 1,
          includePartialCover: true,
          includePartialManifest: true,
        },
      ],
    } as never);
    vi.mocked(services.institutionalReportsApi.preview).mockResolvedValue({ data: mockManifest } as never);

    const user = userEvent.setup();
    render(<ReportBuilderPage />);

    await waitFor(() => {
      expect(screen.getByRole('option', { name: 'قالب ناقص' })).toBeInTheDocument();
    });

    await user.selectOptions(screen.getByLabelText('القالب المحفوظ'), '8');
    await user.click(screen.getByRole('button', { name: 'معاينة التقرير' }));

    await waitFor(() => expect(services.institutionalReportsApi.preview).toHaveBeenCalled());
    const request = vi.mocked(services.institutionalReportsApi.preview).mock.calls[0][0];
    expect(request.sectionIds).toEqual([]);
    expect(request.filters.dateFrom).toBeNull();
    expect(request.filters.dateTo).toBeNull();
    expect(request.filters.departmentIds).toEqual([]);
    expect(request.filters.partyIds).toEqual([]);
    expect(request.filters.categoryIds).toEqual([]);
    expect(request.filters.priorities).toEqual([]);
    expect(request.filters.statuses).toEqual([]);
    expect(request.filters.includeOverdue).toBe(false);
    expect(request.filters.search).toBeNull();
  });

  it('saves current report builder settings as a template', async () => {
    vi.mocked(services.institutionalReportsApi.saveTemplate).mockResolvedValueOnce({
      data: {
        id: 9,
        name: 'قالب جديد',
        reportType: 1,
        sectionIds: [ReportSectionId.Cover],
        defaultFilters: {
          dateFrom: null,
          dateTo: null,
          departmentIds: [],
          partyIds: [],
          categoryIds: [],
          priorities: [],
          statuses: [],
          includeOverdue: false,
          search: null,
        },
        defaultFormat: ExportFormat.Pdf,
        pageNumberingMode: 1,
        includePartialCover: true,
        includePartialManifest: true,
      },
    } as never);

    const user = userEvent.setup();
    render(<ReportBuilderPage />);

    await user.type(screen.getByLabelText('حفظ الإعدادات كقالب'), 'قالب جديد');
    await user.click(screen.getByRole('button', { name: 'حفظ' }));

    await waitFor(() => expect(services.institutionalReportsApi.saveTemplate).toHaveBeenCalled());
    const payload = vi.mocked(services.institutionalReportsApi.saveTemplate).mock.calls[0][0];
    expect(payload.name).toBe('قالب جديد');
    expect(payload.reportType).toBe(InstitutionalReportType.ExecutiveComprehensive);
    expect(payload.defaultFilters.includeOverdue).toBe(false);
    expect(payload.defaultFormat).toBe(ExportFormat.Pdf);
  });

  it('prevents double submission while saving template', async () => {
    let resolveSave: (value: unknown) => void = () => undefined;
    vi.mocked(services.institutionalReportsApi.saveTemplate).mockReturnValueOnce(new Promise((resolve) => {
      resolveSave = resolve;
    }) as never);

    const user = userEvent.setup();
    render(<ReportBuilderPage />);

    await user.type(screen.getByLabelText('حفظ الإعدادات كقالب'), 'قالب بطيء');
    const saveButton = screen.getByRole('button', { name: 'حفظ' });

    fireEvent.click(saveButton);
    fireEvent.click(saveButton);

    await waitFor(() => {
      expect(services.institutionalReportsApi.saveTemplate).toHaveBeenCalledTimes(1);
      expect(screen.getByRole('button', { name: 'جاري الحفظ...' })).toBeDisabled();
    });

    resolveSave({
      data: {
        id: 10,
        name: 'قالب بطيء',
        reportType: 1,
        sectionIds: [ReportSectionId.Cover],
        defaultFilters: {
          dateFrom: null,
          dateTo: null,
          departmentIds: [],
          partyIds: [],
          categoryIds: [],
          priorities: [],
          statuses: [],
          includeOverdue: false,
          search: null,
        },
        defaultFormat: ExportFormat.Pdf,
        pageNumberingMode: 1,
        includePartialCover: true,
        includePartialManifest: true,
      },
    });
  });

  it('sends null search when field is empty', async () => {
    vi.mocked(services.institutionalReportsApi.preview).mockResolvedValue({ data: mockManifest } as never);
    const user = userEvent.setup();
    render(<ReportBuilderPage />);
    await user.click(screen.getByRole('button', { name: 'معاينة التقرير' }));
    await waitFor(() => expect(services.institutionalReportsApi.preview).toHaveBeenCalled());
    const request = vi.mocked(services.institutionalReportsApi.preview).mock.calls[0][0];
    expect(request.filters?.search).toBeNull();
  });

  it('max attributes on limit inputs match backend defaults', () => {
    render(<ReportBuilderPage />);
    expect(screen.getByLabelText('الحد الأقصى للنتائج')).toHaveAttribute('max', '5');
    expect(screen.getByLabelText('الحد الأقصى للحالات الحرجة')).toHaveAttribute('max', '10');
    expect(screen.getByLabelText('الحد الأقصى للتوصيات')).toHaveAttribute('max', '10');
  });

  it('shows export correlation id from response header when blob body omits it', async () => {
    vi.mocked(services.institutionalReportsApi.preview).mockResolvedValue({ data: mockManifest } as never);
    const exportError = new axios.AxiosError(
      'Request failed',
      'ERR_BAD_RESPONSE',
      undefined,
      undefined,
      {
        status: 500,
        statusText: 'Internal Server Error',
        headers: { 'x-correlation-id': 'corr-blob-header' },
        config: { headers: new axios.AxiosHeaders() },
        data: new Blob([JSON.stringify({ message: 'تعذر تصدير التقرير.' })], { type: 'application/json' }),
      },
    );
    vi.mocked(services.institutionalReportsApi.export).mockRejectedValueOnce(exportError);

    const user = userEvent.setup();
    render(<ReportBuilderPage />);

    await user.click(screen.getByRole('button', { name: 'معاينة التقرير' }));
    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'تصدير' })).not.toBeDisabled();
    });

    await user.click(screen.getByRole('button', { name: 'تصدير' }));
    await user.click(screen.getByRole('button', { name: 'تنفيذ التصدير' }));

    await waitFor(() => {
      expect(screen.getByText(/رقم التتبع: corr-blob-header/)).toBeInTheDocument();
    });
  });

  it('includes DepartmentTransactions in report type options', () => {
    render(<ReportBuilderPage />);
    const select = screen.getByLabelText('نوع التقرير');
    const optionTexts = Array.from(select.querySelectorAll('option')).map((o) => o.textContent);
    expect(optionTexts).toContain('تقرير معاملات إدارة');
  });

  it('blocks preview when DepartmentTransactions is selected with no departments', async () => {
    vi.mocked(services.departmentsApi.lookup).mockResolvedValueOnce(
      mockLookupItems([{ id: 20, name: 'الإدارة ب' }]),
    );
    const user = userEvent.setup();
    render(<ReportBuilderPage />);

    await user.selectOptions(screen.getByLabelText('نوع التقرير'), String(InstitutionalReportType.DepartmentTransactions));
    await user.click(screen.getByRole('button', { name: 'معاينة التقرير' }));

    expect(services.institutionalReportsApi.preview).not.toHaveBeenCalled();
    await waitFor(() => {
      expect(screen.getAllByText('يجب تحديد إدارة واحدة على الأقل لتقرير معاملات إدارة.').length).toBeGreaterThan(0);
    });
  });

  it('sends detailSortBy and groupDetailsByDepartment in the preview request', async () => {
    vi.mocked(services.departmentsApi.lookup).mockResolvedValueOnce(
      mockLookupItems([{ id: 20, name: 'الإدارة ب' }, { id: 30, name: 'الإدارة ج' }]),
    );
    vi.mocked(services.institutionalReportsApi.preview).mockResolvedValue(mockPreviewResponse());
    const user = userEvent.setup();
    render(<ReportBuilderPage />);

    await user.selectOptions(screen.getByLabelText('نوع التقرير'), String(InstitutionalReportType.DepartmentTransactions));
    await waitFor(() => expect(screen.getByLabelText(/الإدارات المحالة\/الصادر لها/)).toBeInTheDocument());
    await user.selectOptions(screen.getByLabelText(/الإدارات المحالة\/الصادر لها/), ['20', '30']);
    await user.selectOptions(screen.getByLabelText('ترتيب التفاصيل'), String(3));
    await user.click(screen.getByRole('checkbox', { name: /تجميع التفاصيل حسب الإدارة/i }));
    await user.click(screen.getByRole('button', { name: 'معاينة التقرير' }));

    await waitFor(() => expect(services.institutionalReportsApi.preview).toHaveBeenCalled());
    const request = vi.mocked(services.institutionalReportsApi.preview).mock.calls[0][0];
    expect(request.detailSortBy).toBe(3);
    expect(request.groupDetailsByDepartment).toBe(true);
  });

  it('resets groupDetailsByDepartment when switching away from DepartmentTransactions', async () => {
    vi.mocked(services.departmentsApi.lookup).mockResolvedValueOnce(
      mockLookupItems([{ id: 20, name: 'الإدارة ب' }, { id: 30, name: 'الإدارة ج' }]),
    );
    vi.mocked(services.institutionalReportsApi.preview).mockResolvedValue(mockPreviewResponse());
    const user = userEvent.setup();
    render(<ReportBuilderPage />);

    await user.selectOptions(screen.getByLabelText('نوع التقرير'), String(InstitutionalReportType.DepartmentTransactions));
    await waitFor(() => expect(screen.getByLabelText(/الإدارات المحالة\/الصادر لها/)).toBeInTheDocument());
    await user.selectOptions(screen.getByLabelText(/الإدارات المحالة\/الصادر لها/), ['20', '30']);
    await user.click(screen.getByRole('checkbox', { name: /تجميع التفاصيل حسب الإدارة/i }));

    await user.selectOptions(screen.getByLabelText('نوع التقرير'), String(InstitutionalReportType.ExecutiveComprehensive));
    await user.click(screen.getByRole('button', { name: 'معاينة التقرير' }));

    await waitFor(() => expect(services.institutionalReportsApi.preview).toHaveBeenCalled());
    const request = vi.mocked(services.institutionalReportsApi.preview).mock.calls[0][0];
    expect(request.groupDetailsByDepartment).toBe(false);
  });

  it('restores detailSortBy and groupDetailsByDepartment when applying a saved template', async () => {
    const template: ReportTemplate = {
      id: 10,
      name: 'قالب معاملات إدارة',
      reportType: InstitutionalReportType.DepartmentTransactions,
      sectionIds: [ReportSectionId.Cover, ReportSectionId.TransactionDetails],
      defaultFilters: { ...emptyDeptTransactionsFilters, departmentIds: [20, 30] },
      defaultFormat: ExportFormat.Pdf,
      pageNumberingMode: 1,
      includePartialCover: false,
      includePartialManifest: false,
      detailSortBy: 2,
      groupDetailsByDepartment: true,
    };
    vi.mocked(services.institutionalReportsApi.getTemplates).mockResolvedValueOnce(mockAxiosResponse([template]));
    vi.mocked(services.institutionalReportsApi.preview).mockResolvedValue(mockPreviewResponse());

    const user = userEvent.setup();
    render(<ReportBuilderPage />);

    await waitFor(() => {
      expect(screen.getByRole('option', { name: 'قالب معاملات إدارة' })).toBeInTheDocument();
    });

    await user.selectOptions(screen.getByLabelText('القالب المحفوظ'), '10');
    await user.click(screen.getByRole('button', { name: 'معاينة التقرير' }));

    await waitFor(() => expect(services.institutionalReportsApi.preview).toHaveBeenCalled());
    const request = vi.mocked(services.institutionalReportsApi.preview).mock.calls[0][0];
    expect(request.detailSortBy).toBe(2);
    expect(request.groupDetailsByDepartment).toBe(true);
  });

  it('includes detailSortBy and groupDetailsByDepartment in the saveTemplate payload', async () => {
    vi.mocked(services.departmentsApi.lookup).mockResolvedValueOnce(
      mockLookupItems([{ id: 20, name: 'الإدارة ب' }, { id: 30, name: 'الإدارة ج' }]),
    );
    const savedTemplate: ReportTemplate = {
      id: 11,
      name: 'قالب جديد',
      reportType: InstitutionalReportType.DepartmentTransactions,
      sectionIds: [ReportSectionId.Cover],
      defaultFilters: { ...emptyDeptTransactionsFilters, departmentIds: [20, 30] },
      defaultFormat: ExportFormat.Pdf,
      pageNumberingMode: 1,
      includePartialCover: true,
      includePartialManifest: true,
      detailSortBy: 2,
      groupDetailsByDepartment: true,
    };
    vi.mocked(services.institutionalReportsApi.saveTemplate).mockResolvedValueOnce(mockAxiosResponse(savedTemplate));

    const user = userEvent.setup();
    render(<ReportBuilderPage />);

    await user.selectOptions(screen.getByLabelText('نوع التقرير'), String(InstitutionalReportType.DepartmentTransactions));
    await waitFor(() => expect(screen.getByLabelText(/الإدارات المحالة\/الصادر لها/)).toBeInTheDocument());
    await user.selectOptions(screen.getByLabelText(/الإدارات المحالة\/الصادر لها/), ['20', '30']);
    await user.selectOptions(screen.getByLabelText('ترتيب التفاصيل'), String(2));
    await user.click(screen.getByRole('checkbox', { name: /تجميع التفاصيل حسب الإدارة/i }));

    await user.type(screen.getByLabelText('حفظ الإعدادات كقالب'), 'قالب جديد');
    await user.click(screen.getByRole('button', { name: 'حفظ' }));

    await waitFor(() => expect(services.institutionalReportsApi.saveTemplate).toHaveBeenCalled());
    const payload = vi.mocked(services.institutionalReportsApi.saveTemplate).mock.calls[0][0];
    expect(payload.detailSortBy).toBe(2);
    expect(payload.groupDetailsByDepartment).toBe(true);
  });

  it('shows the relabeled departments filter with its clarifying hint text', async () => {
    vi.mocked(services.departmentsApi.lookup).mockResolvedValueOnce(
      mockLookupItems([{ id: 20, name: 'الإدارة ب' }]),
    );
    render(<ReportBuilderPage />);
    await waitFor(() => {
      expect(screen.getByText(/الإدارات المحالة\/الصادر لها/)).toBeInTheDocument();
    });
    expect(screen.getByText(/يطابق هذا الفلتر الإدارات المرتبطة بالمعاملة عبر الإحالات أو الإدارات الصادر لها/)).toBeInTheDocument();
  });

  it('shows the overdue DateFrom hint only when OverdueTransactions is selected', async () => {
    const user = userEvent.setup();
    render(<ReportBuilderPage />);

    expect(screen.queryByText(/في تقرير المتأخرات، يتم تقييم التأخر حتى تاريخ نهاية الفترة/)).not.toBeInTheDocument();

    await user.selectOptions(screen.getByLabelText('نوع التقرير'), String(InstitutionalReportType.OverdueTransactions));

    expect(screen.getByText(/في تقرير المتأخرات، يتم تقييم التأخر حتى تاريخ نهاية الفترة/)).toBeInTheDocument();
  });
});
