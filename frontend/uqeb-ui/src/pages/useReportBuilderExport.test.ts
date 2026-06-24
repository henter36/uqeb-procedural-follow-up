import { describe, expect, it, vi, beforeEach } from 'vitest';
import { act, renderHook, waitFor } from '@testing-library/react';
import axios from 'axios';
import {
  getExportErrorDetails,
  getExportPreconditionError,
  resolveExportErrorMessage,
  useReportBuilderExport,
} from './useReportBuilderExport';
import { DetailOverflowAction, ExportFormat, ReportSectionId } from '../api/institutionalReports.constants';
import type { InstitutionalReportManifest, ReportBuildRequest } from '../api/institutionalReports.types';
import * as services from '../api/services';
import { downloadBlob } from '../utils/downloadBlob';
import type { ApiErrorDetails } from '../utils/apiHelpers';

vi.mock('../api/services', () => ({
  institutionalReportsApi: {
    export: vi.fn(),
  },
}));

vi.mock('../utils/downloadBlob', () => ({
  downloadBlob: vi.fn(),
}));

const baseManifest: InstitutionalReportManifest = {
  reportId: 'report-1',
  totalPages: 1,
  pages: [{
    renderedPageNumber: 1,
    originalPageNumber: 1,
    sectionId: ReportSectionId.Cover,
    sectionName: 'الغلاف',
    pageTitle: 'الغلاف',
    htmlContent: '<p>1</p>',
    isSelectable: true,
  }],
  requiresDetailOverflowAction: false,
};

const defaultBuildRequest = (): ReportBuildRequest => ({
  reportType: 1,
  sectionIds: [ReportSectionId.Cover],
  filters: {
    departmentIds: [],
    partyIds: [],
    categoryIds: [],
    priorities: [],
    statuses: [],
    includeJointDepartmentTransactions: false,
    includeOverdue: false,
    includeDetails: false,
    includeRisks: false,
    includeRecommendations: false,
  },
});

function createHookOptions(overrides: Partial<Parameters<typeof useReportBuilderExport>[0]> = {}) {
  const setLoading = vi.fn();
  const setError = vi.fn();
  const setErrorCorrelationId = vi.fn();
  const buildRequest = vi.fn(defaultBuildRequest);

  const options = {
    buildRequest,
    manifest: baseManifest,
    sectionIds: [ReportSectionId.Cover],
    selectedPages: [] as number[],
    pageSelectionMode: 'thumbnails' as const,
    pageRange: '',
    currentPage: 1,
    setLoading,
    setError,
    setErrorCorrelationId,
    ...overrides,
  };

  const hook = renderHook(() => useReportBuilderExport(options));

  return { ...hook, setLoading, setError, setErrorCorrelationId, buildRequest };
}

describe('getExportPreconditionError', () => {
  it('returns overflow choice message when action is not selected', () => {
    expect(getExportPreconditionError(true, DetailOverflowAction.None, ExportFormat.Pdf))
      .toBe('يتجاوز التقرير حد صفوف التفاصيل. اختر كيفية التعامل مع التفاصيل قبل التصدير.');
  });

  it('returns split PDF format message for non-PDF export', () => {
    expect(getExportPreconditionError(true, DetailOverflowAction.SplitPdf, ExportFormat.Html))
      .toBe('تقسيم التفاصيل إلى عدة ملفات PDF متاح فقط عند اختيار صيغة PDF.');
  });

  it('returns null when overflow choice is not required', () => {
    expect(getExportPreconditionError(false, DetailOverflowAction.None, ExportFormat.Pdf)).toBeNull();
  });
});

describe('resolveExportErrorMessage', () => {
  const emptyDetails: ApiErrorDetails = {
    message: '',
    title: '',
    detail: '',
    errorCode: '',
    correlationId: '',
    validationErrors: {},
    httpStatus: null,
  };

  it('prefers detailOverflowAction validation message', () => {
    expect(resolveExportErrorMessage({
      ...emptyDetails,
      validationErrors: { detailOverflowAction: 'اختر إجراء التفاصيل.' },
      message: 'تعذر تصدير التقرير.',
    })).toBe('اختر إجراء التفاصيل.');
  });

  it('appends distinct backend message to default export failure text', () => {
    expect(resolveExportErrorMessage({
      ...emptyDetails,
      message: 'انتهت مهلة تصدير التقرير.',
    })).toBe('تعذر تصدير التقرير. انتهت مهلة تصدير التقرير.');
  });

  it('returns default message when backend message matches default', () => {
    expect(resolveExportErrorMessage({
      ...emptyDetails,
      message: 'تعذر تصدير التقرير.',
    })).toBe('تعذر تصدير التقرير.');
  });
});

describe('getExportErrorDetails', () => {
  it('uses axios details for non-blob errors', async () => {
    const error = new axios.AxiosError(
      'Request failed',
      'ERR_BAD_RESPONSE',
      undefined,
      undefined,
      {
        status: 503,
        statusText: 'Service Unavailable',
        headers: { 'x-correlation-id': 'corr-axios' },
        config: { headers: new axios.AxiosHeaders() },
        data: { message: 'انتهت مهلة تصدير التقرير.', errorCode: 'export_timeout' },
      },
    );

    const details = await getExportErrorDetails(error);

    expect(details.correlationId).toBe('corr-axios');
    expect(details.httpStatus).toBe(503);
    expect(details.message).toContain('انتهت مهلة تصدير التقرير.');
  });

  it('falls back to header correlation id when blob body omits it', async () => {
    const error = new axios.AxiosError(
      'Request failed',
      'ERR_BAD_RESPONSE',
      undefined,
      undefined,
      {
        status: 500,
        statusText: 'Internal Server Error',
        headers: { 'x-correlation-id': 'corr-header' },
        config: { headers: new axios.AxiosHeaders() },
        data: new Blob([JSON.stringify({ message: 'تعذر تصدير التقرير.' })], { type: 'application/json' }),
      },
    );

    const details = await getExportErrorDetails(error);

    expect(details.correlationId).toBe('corr-header');
    expect(details.httpStatus).toBe(500);
  });

  it('prefers blob body correlation id over header', async () => {
    const error = new axios.AxiosError(
      'Request failed',
      'ERR_BAD_RESPONSE',
      undefined,
      undefined,
      {
        status: 500,
        statusText: 'Internal Server Error',
        headers: { 'x-correlation-id': 'corr-header' },
        config: { headers: new axios.AxiosHeaders() },
        data: new Blob(
          [JSON.stringify({ message: 'تعذر تصدير التقرير.', correlationId: 'corr-body' })],
          { type: 'application/json' },
        ),
      },
    );

    const details = await getExportErrorDetails(error);

    expect(details.correlationId).toBe('corr-body');
  });

  it('reads detailOverflowAction validation errors from blob body', async () => {
    const error = new axios.AxiosError(
      'Request failed',
      'ERR_BAD_RESPONSE',
      undefined,
      undefined,
      {
        status: 400,
        statusText: 'Bad Request',
        headers: {},
        config: { headers: new axios.AxiosHeaders() },
        data: new Blob(
          [JSON.stringify({
            message: 'تعذر تصدير التقرير.',
            errors: { detailOverflowAction: 'اختر إجراء التفاصيل.' },
          })],
          { type: 'application/json' },
        ),
      },
    );

    const details = await getExportErrorDetails(error);

    expect(details.validationErrors.detailOverflowAction).toBe('اختر إجراء التفاصيل.');
  });
});

describe('useReportBuilderExport.runExport', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('shows precondition error when overflow action is missing', async () => {
    const manifest = { ...baseManifest, requiresDetailOverflowAction: true };
    const { result, setError, setLoading } = createHookOptions({
      manifest,
      sectionIds: [ReportSectionId.TransactionDetails],
    });

    act(() => {
      result.current.setExportFormat(ExportFormat.Pdf);
    });

    await act(async () => {
      await result.current.runExport();
    });

    expect(setError).toHaveBeenCalledWith(
      'يتجاوز التقرير حد صفوف التفاصيل. اختر كيفية التعامل مع التفاصيل قبل التصدير.',
    );
    expect(vi.mocked(services.institutionalReportsApi.export)).not.toHaveBeenCalled();
    expect(setLoading).toHaveBeenCalledWith(true);
    expect(setLoading).toHaveBeenCalledWith(false);
  });

  it('shows precondition error when split PDF is selected for non-PDF format', async () => {
    const manifest = { ...baseManifest, requiresDetailOverflowAction: true };
    const { result, setError } = createHookOptions({
      manifest,
      sectionIds: [ReportSectionId.TransactionDetails],
    });

    act(() => {
      result.current.setExportFormat(ExportFormat.Html);
      result.current.setDetailOverflowAction(DetailOverflowAction.SplitPdf);
    });

    await act(async () => {
      await result.current.runExport();
    });

    expect(setError).toHaveBeenCalledWith(
      'تقسيم التفاصيل إلى عدة ملفات PDF متاح فقط عند اختيار صيغة PDF.',
    );
    expect(vi.mocked(services.institutionalReportsApi.export)).not.toHaveBeenCalled();
  });

  it('downloads file and closes dialog on successful export', async () => {
    vi.mocked(services.institutionalReportsApi.export).mockResolvedValue({
      data: new Uint8Array([1, 2, 3]),
      headers: { 'content-type': 'application/pdf' },
    } as never);

    const { result } = createHookOptions();

    act(() => {
      result.current.openExportDialog();
    });

    expect(result.current.exportOpen).toBe(true);

    await act(async () => {
      await result.current.runExport();
    });

    expect(downloadBlob).toHaveBeenCalled();
    expect(result.current.exportOpen).toBe(false);
  });

  it('keeps dialog open and surfaces blob validation error', async () => {
    vi.mocked(services.institutionalReportsApi.export).mockRejectedValue(
      new axios.AxiosError(
        'Request failed',
        'ERR_BAD_RESPONSE',
        undefined,
        undefined,
        {
          status: 400,
          statusText: 'Bad Request',
          headers: {},
          config: { headers: new axios.AxiosHeaders() },
          data: new Blob(
            [JSON.stringify({
              message: 'تعذر تصدير التقرير.',
              errors: { detailOverflowAction: 'اختر إجراء التفاصيل.' },
            })],
            { type: 'application/json' },
          ),
        },
      ),
    );

    const { result, setError } = createHookOptions();

    act(() => {
      result.current.openExportDialog();
    });

    await act(async () => {
      await result.current.runExport();
    });

    await waitFor(() => {
      expect(setError).toHaveBeenCalledWith('اختر إجراء التفاصيل.');
    });
    expect(result.current.exportOpen).toBe(true);
  });

  it('sets correlation id from header when blob body omits it', async () => {
    vi.mocked(services.institutionalReportsApi.export).mockRejectedValue(
      new axios.AxiosError(
        'Request failed',
        'ERR_BAD_RESPONSE',
        undefined,
        undefined,
        {
          status: 500,
          statusText: 'Internal Server Error',
          headers: { 'x-correlation-id': 'corr-export-header' },
          config: { headers: new axios.AxiosHeaders() },
          data: new Blob([JSON.stringify({ message: 'تعذر تصدير التقرير.' })], { type: 'application/json' }),
        },
      ),
    );

    const { result, setErrorCorrelationId } = createHookOptions();

    await act(async () => {
      await result.current.runExport();
    });

    await waitFor(() => {
      expect(setErrorCorrelationId).toHaveBeenCalledWith('corr-export-header');
    });
  });

  it('uses axios error details for non-blob export failures', async () => {
    vi.mocked(services.institutionalReportsApi.export).mockRejectedValue(
      new axios.AxiosError(
        'Request failed',
        'ERR_BAD_RESPONSE',
        undefined,
        undefined,
        {
          status: 503,
          statusText: 'Service Unavailable',
          headers: { 'x-correlation-id': 'corr-plain' },
          config: { headers: new axios.AxiosHeaders() },
          data: { message: 'انتهت مهلة تصدير التقرير.' },
        },
      ),
    );

    const { result, setError } = createHookOptions();

    await act(async () => {
      await result.current.runExport();
    });

    await waitFor(() => {
      expect(setError).toHaveBeenCalledWith('تعذر تصدير التقرير. انتهت مهلة تصدير التقرير.');
    });
  });

  it('always clears loading state after export attempt', async () => {
    const { result, setLoading } = createHookOptions({
      manifest: { ...baseManifest, requiresDetailOverflowAction: true },
      sectionIds: [ReportSectionId.TransactionDetails],
    });

    await act(async () => {
      await result.current.runExport();
    });

    expect(setLoading.mock.calls).toEqual([[true], [false]]);
  });
});
