import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { cleanup, render, screen, waitFor, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import DepartmentTransactionsPage from './DepartmentTransactionsPage';
import * as services from '../api/services';
import type { DepartmentResponseSummaryDto, DepartmentResponseDto } from '../api/types';

vi.mock('../api/services', () => ({
  departmentResponsesApi: {
    getMyResponses: vi.fn(),
    getById: vi.fn(),
    create: vi.fn(),
    update: vi.fn(),
    submit: vi.fn(),
    uploadAttachment: vi.fn(),
    deleteAttachment: vi.fn(),
    downloadAttachment: vi.fn(),
  },
}));

vi.mock('../context/useAuth', () => ({
  useAuth: () => ({ user: { role: 'DepartmentUser', departmentId: 1 } }),
}));

const summaryDraft: DepartmentResponseSummaryDto = {
  id: 1,
  transactionId: 10,
  transactionSubject: 'موضوع الاختبار',
  internalTrackingNumber: 'TX-001',
  departmentId: 1,
  departmentName: 'إدارة التجارب',
  status: 'Draft',
  submittedAt: undefined,
  createdAt: '2026-06-01T00:00:00Z',
};

const detailDraft: DepartmentResponseDto = {
  id: 1,
  transactionId: 10,
  transactionSubject: 'موضوع الاختبار',
  internalTrackingNumber: 'TX-001',
  departmentId: 1,
  departmentName: 'إدارة التجارب',
  responseText: 'نص الرد',
  status: 'Draft',
  submittedByName: 'موظف',
  submittedAt: undefined,
  reviewedByName: undefined,
  reviewedAt: undefined,
  reviewNote: undefined,
  createdAt: '2026-06-01T00:00:00Z',
  updatedAt: undefined,
  attachments: [],
};

const mockApi = vi.mocked(services.departmentResponsesApi);

function renderPage() {
  return render(
    <MemoryRouter>
      <DepartmentTransactionsPage />
    </MemoryRouter>
  );
}

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

describe('DepartmentTransactionsPage', () => {
  beforeEach(() => {
    mockApi.getMyResponses.mockResolvedValue({ data: [summaryDraft] } as never);
  });

  it('renders list of department responses on load', async () => {
    renderPage();
    await waitFor(() => {
      expect(screen.getByText('TX-001')).toBeTruthy();
      expect(screen.getByText('موضوع الاختبار')).toBeTruthy();
    });
  });

  it('shows empty state when no responses', async () => {
    mockApi.getMyResponses.mockResolvedValueOnce({ data: [] } as never);
    renderPage();
    await waitFor(() => {
      expect(screen.getByText('لا توجد ردود إدارة بعد')).toBeTruthy();
    });
  });

  it('opens detail view on row click', async () => {
    mockApi.getById.mockResolvedValue({ data: detailDraft } as never);
    renderPage();
    await waitFor(() => screen.getByText('TX-001'));
    fireEvent.click(screen.getByText('عرض'));
    await waitFor(() => {
      expect(screen.getByText('نص الرد')).toBeTruthy();
    });
  });

  it('shows submit button for Draft responses', async () => {
    mockApi.getById.mockResolvedValue({ data: detailDraft } as never);
    renderPage();
    await waitFor(() => screen.getByText('TX-001'));
    fireEvent.click(screen.getByText('عرض'));
    await waitFor(() => {
      expect(screen.getByText('تقديم للمراجعة')).toBeTruthy();
    });
  });

  it('does not show edit controls for Approved responses', async () => {
    mockApi.getById.mockResolvedValue({
      data: { ...detailDraft, status: 'Approved' },
    } as never);
    renderPage();
    await waitFor(() => screen.getByText('TX-001'));
    fireEvent.click(screen.getByText('عرض'));
    await waitFor(() => {
      expect(screen.queryByText('تقديم للمراجعة')).toBeNull();
      expect(screen.queryByText('حفظ التعديلات')).toBeNull();
    });
  });

  it('shows review note when ReturnedForCorrection', async () => {
    mockApi.getById.mockResolvedValue({
      data: { ...detailDraft, status: 'ReturnedForCorrection', reviewNote: 'يحتاج إصلاح' },
    } as never);
    renderPage();
    await waitFor(() => screen.getByText('TX-001'));
    fireEvent.click(screen.getByText('عرض'));
    await waitFor(() => {
      expect(screen.getByText(/يحتاج إصلاح/)).toBeTruthy();
    });
  });
});
