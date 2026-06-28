import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { cleanup, render, screen, waitFor, fireEvent } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import DepartmentTransactionsPage from './DepartmentTransactionsPage';
import * as services from '../api/services';
import type { DepartmentTransactionResponseItemDto, DepartmentResponseDto } from '../api/types';

vi.mock('../api/services', () => ({
  departmentResponsesApi: {
    getDepartmentTransactions: vi.fn(),
    getById: vi.fn(),
    create: vi.fn(),
    update: vi.fn(),
    submit: vi.fn(),
    uploadAttachment: vi.fn(),
    deleteAttachment: vi.fn(),
    downloadAttachment: vi.fn(),
  },
}));

const txNoResponse: DepartmentTransactionResponseItemDto = {
  transactionId: 10,
  internalTrackingNumber: 'TX-001',
  subject: 'موضوع الاختبار',
  incomingDate: '2026-01-01T00:00:00Z',
  priority: 'Normal',
  assignedDate: '2026-06-01T00:00:00Z',
  departmentId: 1,
  departmentName: 'إدارة التجارب',
  departmentResponseId: undefined,
  departmentResponseStatus: undefined,
  canCreateResponse: true,
  canEditResponse: false,
  canSubmitResponse: false,
};

const txWithDraft: DepartmentTransactionResponseItemDto = {
  ...txNoResponse,
  departmentResponseId: 1,
  departmentResponseStatus: 'Draft',
  canCreateResponse: false,
  canEditResponse: true,
  canSubmitResponse: true,
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
    mockApi.getDepartmentTransactions.mockResolvedValue({ data: [txWithDraft] } as never);
  });

  it('renders list of department transactions on load', async () => {
    renderPage();
    await waitFor(() => {
      expect(screen.getByText('TX-001')).toBeTruthy();
      expect(screen.getByText('موضوع الاختبار')).toBeTruthy();
    });
  });

  it('shows empty state when no transactions assigned', async () => {
    mockApi.getDepartmentTransactions.mockResolvedValueOnce({ data: [] } as never);
    renderPage();
    await waitFor(() => {
      expect(screen.getByText(/لا توجد معاملات مسندة/)).toBeTruthy();
    });
  });

  it('shows create button for transaction without response', async () => {
    mockApi.getDepartmentTransactions.mockResolvedValueOnce({ data: [txNoResponse] } as never);
    renderPage();
    await waitFor(() => {
      expect(screen.getByText('تسجيل إفادة')).toBeTruthy();
    });
  });

  it('shows view button for transaction with an existing response', async () => {
    renderPage();
    await waitFor(() => {
      expect(screen.getByText('تعديل الإفادة')).toBeTruthy();
    });
  });

  it('opens create form from transaction row with no manual transaction ID input', async () => {
    mockApi.getDepartmentTransactions.mockResolvedValueOnce({ data: [txNoResponse] } as never);
    renderPage();
    await waitFor(() => screen.getByText('تسجيل إفادة'));
    fireEvent.click(screen.getByText('تسجيل إفادة'));
    await waitFor(() => {
      expect(screen.getByText(/موضوع الاختبار/)).toBeTruthy();
      // no manual transaction ID field
      expect(screen.queryByPlaceholderText(/أدخل رقم المعاملة/)).toBeNull();
    });
  });

  it('opens detail view on clicking view button', async () => {
    mockApi.getById.mockResolvedValue({ data: detailDraft } as never);
    renderPage();
    await waitFor(() => screen.getByText('تعديل الإفادة'));
    fireEvent.click(screen.getByText('تعديل الإفادة'));
    await waitFor(() => {
      expect(screen.getByDisplayValue('نص الرد')).toBeTruthy();
    });
  });

  it('shows submit button for Draft responses', async () => {
    mockApi.getById.mockResolvedValue({ data: detailDraft } as never);
    renderPage();
    await waitFor(() => screen.getByText('تعديل الإفادة'));
    fireEvent.click(screen.getByText('تعديل الإفادة'));
    await waitFor(() => {
      expect(screen.getByText('تقديم للمراجعة')).toBeTruthy();
    });
  });

  it('does not show edit controls for Approved responses', async () => {
    mockApi.getById.mockResolvedValue({
      data: { ...detailDraft, status: 'Approved' },
    } as never);
    renderPage();
    await waitFor(() => screen.getByText('تعديل الإفادة'));
    fireEvent.click(screen.getByText('تعديل الإفادة'));
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
    await waitFor(() => screen.getByText('تعديل الإفادة'));
    fireEvent.click(screen.getByText('تعديل الإفادة'));
    await waitFor(() => {
      expect(screen.getByText(/يحتاج إصلاح/)).toBeTruthy();
    });
  });

  it('form text does not leak between responses', async () => {
    const detail2: DepartmentResponseDto = { ...detailDraft, id: 2, responseText: 'نص ثانٍ' };
    mockApi.getById
      .mockResolvedValueOnce({ data: detailDraft } as never)
      .mockResolvedValue({ data: detail2 } as never);
    renderPage();
    await waitFor(() => screen.getByText('تعديل الإفادة'));
    fireEvent.click(screen.getByText('تعديل الإفادة'));
    await waitFor(() => screen.getByText('رجوع للقائمة'));
    fireEvent.click(screen.getByText('رجوع للقائمة'));
    await waitFor(() => screen.getByText('تعديل الإفادة'));
    fireEvent.click(screen.getByText('تعديل الإفادة'));
    await waitFor(() => {
      expect(screen.getByDisplayValue('نص ثانٍ')).toBeTruthy();
    });
  });
});
