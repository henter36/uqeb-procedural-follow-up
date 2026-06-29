import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { cleanup, render, screen, waitFor, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import DepartmentResponseReviewPage from './DepartmentResponseReviewPage';
import * as services from '../api/services';
import type { DepartmentResponseSummaryDto, DepartmentResponseDto } from '../api/types';

vi.mock('../api/services', () => ({
  departmentResponsesApi: {
    getPendingReview: vi.fn(),
    getById: vi.fn(),
    approve: vi.fn(),
    returnForCorrection: vi.fn(),
    reject: vi.fn(),
    downloadAttachment: vi.fn(),
  },
}));

const pendingItem: DepartmentResponseSummaryDto = {
  id: 5,
  transactionId: 20,
  transactionSubject: 'موضوع المراجعة',
  internalTrackingNumber: 'TX-020',
  departmentId: 2,
  departmentName: 'إدارة الموارد',
  status: 'SubmittedForReview',
  submittedAt: '2026-06-10T10:00:00Z',
  createdAt: '2026-06-09T00:00:00Z',
};

const pendingDetail: DepartmentResponseDto = {
  id: 5,
  transactionId: 20,
  transactionSubject: 'موضوع المراجعة',
  internalTrackingNumber: 'TX-020',
  departmentId: 2,
  departmentName: 'إدارة الموارد',
  responseText: 'الرد الرسمي من الإدارة',
  status: 'SubmittedForReview',
  submittedByName: 'موظف الإدارة',
  submittedAt: '2026-06-10T10:00:00Z',
  reviewedByName: undefined,
  reviewedAt: undefined,
  reviewNote: undefined,
  createdAt: '2026-06-09T00:00:00Z',
  updatedAt: undefined,
  attachments: [],
};

const mockApi = vi.mocked(services.departmentResponsesApi);

function apiError(status: number, message = '') {
  return {
    isAxiosError: true,
    message: 'Request failed',
    response: {
      status,
      data: message ? { message } : {},
      headers: {},
    },
  };
}

function renderPage() {
  return render(
    <MemoryRouter>
      <DepartmentResponseReviewPage />
    </MemoryRouter>
  );
}

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

describe('DepartmentResponseReviewPage', () => {
  beforeEach(() => {
    mockApi.getPendingReview.mockResolvedValue({ data: [pendingItem] } as never);
  });

  it('renders pending responses list', async () => {
    renderPage();
    await waitFor(() => {
      expect(screen.getByText('TX-020')).toBeTruthy();
      expect(screen.getByText('موضوع المراجعة')).toBeTruthy();
    });
  });

  it('shows empty state when no pending reviews', async () => {
    mockApi.getPendingReview.mockResolvedValueOnce({ data: [] } as never);
    renderPage();
    await waitFor(() => {
      expect(screen.getByText(/لا توجد إفادات بانتظار المراجعة/)).toBeTruthy();
    });
  });

  it('shows a clear permission message for 403 without retry polling', async () => {
    mockApi.getPendingReview.mockRejectedValueOnce(apiError(403, 'ممنوع') as never);

    renderPage();

    await waitFor(() => {
      expect(screen.getByText('لا تملك صلاحية مراجعة إفادات الإدارات.')).toBeTruthy();
    });
    expect(mockApi.getPendingReview).toHaveBeenCalledTimes(1);
  });

  it('shows a clear route message for 404 without retry polling', async () => {
    mockApi.getPendingReview.mockRejectedValueOnce(apiError(404) as never);

    renderPage();

    await waitFor(() => {
      expect(screen.getByText('مسار مراجعة الإفادات غير متاح. تحقق من إعدادات الخادم.')).toBeTruthy();
    });
    expect(mockApi.getPendingReview).toHaveBeenCalledTimes(1);
  });

  it('opens review detail on row click', async () => {
    mockApi.getById.mockResolvedValue({ data: pendingDetail } as never);
    renderPage();
    await waitFor(() => screen.getByText('TX-020'));
    fireEvent.click(screen.getByText('مراجعة'));
    await waitFor(() => {
      expect(screen.getByText('الرد الرسمي من الإدارة')).toBeTruthy();
    });
  });

  it('shows approve, return, reject buttons for SubmittedForReview', async () => {
    mockApi.getById.mockResolvedValue({ data: pendingDetail } as never);
    renderPage();
    await waitFor(() => screen.getByText('TX-020'));
    fireEvent.click(screen.getByText('مراجعة'));
    await waitFor(() => {
      expect(screen.getByText('قبول')).toBeTruthy();
      expect(screen.getByText('إعادة للتصحيح')).toBeTruthy();
      expect(screen.getByText('رفض')).toBeTruthy();
    });
  });

  it('shows approved status after approval', async () => {
    // Override: first call returns pending list, second call (after approve) returns empty list
    mockApi.getPendingReview
      .mockResolvedValueOnce({ data: [pendingItem] } as never)
      .mockResolvedValue({ data: [] } as never);
    mockApi.getById.mockResolvedValue({ data: pendingDetail } as never);
    mockApi.approve.mockResolvedValue({
      data: { ...pendingDetail, status: 'Approved', reviewedByName: 'المراجع' },
    } as never);

    renderPage();
    await waitFor(() => screen.getByText('TX-020'));
    fireEvent.click(screen.getByText('مراجعة'));
    await waitFor(() => screen.getByText('قبول'));
    fireEvent.click(screen.getByText('قبول'));
    await waitFor(() => {
      expect(screen.queryByText('قبول')).toBeNull();
      expect(screen.getAllByText('معتمد').length).toBeGreaterThan(0);
    });
  });

  it('sends reject request with review note', async () => {
    mockApi.getById.mockResolvedValue({ data: pendingDetail } as never);
    mockApi.reject.mockResolvedValue({
      data: { ...pendingDetail, status: 'Rejected', reviewedByName: 'المراجع', reviewNote: 'غير مكتمل' },
    } as never);
    renderPage();
    await waitFor(() => screen.getByText('TX-020'));
    fireEvent.click(screen.getByText('مراجعة'));
    await waitFor(() => screen.getByText('رفض'));
    fireEvent.change(screen.getByLabelText('ملاحظة المراجع (مطلوبة عند الإعادة أو الرفض)'), {
      target: { value: 'غير مكتمل' },
    });
    fireEvent.click(screen.getByText('رفض'));

    await waitFor(() => {
      expect(mockApi.reject).toHaveBeenCalledWith(5, 'غير مكتمل');
    });
  });

  it('shows error if return attempted without note', async () => {
    mockApi.getById.mockResolvedValue({ data: pendingDetail } as never);
    renderPage();
    await waitFor(() => screen.getByText('TX-020'));
    fireEvent.click(screen.getByText('مراجعة'));
    await waitFor(() => screen.getByText('إعادة للتصحيح'));
    fireEvent.click(screen.getByText('إعادة للتصحيح'));
    await waitFor(() => {
      expect(screen.getByText(/الملاحظة مطلوبة/)).toBeTruthy();
    });
  });
});
