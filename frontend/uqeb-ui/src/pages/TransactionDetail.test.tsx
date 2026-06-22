import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import TransactionDetailPage from './TransactionDetail';
import * as services from '../api/services';

vi.mock('../context/AuthContext', () => ({
  useAuth: () => ({
    canEdit: true,
    canClose: true,
    isDepartmentUser: false,
    user: { fullName: 'مختبر', role: 'Admin' },
    logout: vi.fn(),
    login: vi.fn(),
    isAdmin: true,
  }),
}));

vi.mock('../features/scanner/ScanAttachmentButton', () => ({
  default: () => null,
}));

const baseTx = {
  id: 1,
  internalTrackingNumber: 'TRK-1',
  incomingNumber: 'IN-1',
  incomingDate: '2026-01-01',
  subject: 'موضوع',
  incomingSourceType: 'External',
  incomingFrom: 'جهة',
  categoryName: 'تصنيف',
  outgoingDepartments: [],
  outgoingDepartmentNames: [],
  status: 'Open',
  isOverdue: false,
  requiresResponse: false,
  responseType: 'None',
  responseCompleted: false,
  priority: 'Normal',
  pendingDepartmentNames: [],
  repliedDepartmentNames: [],
  hasPendingAssignments: false,
  daysSinceIncoming: 1,
  daysSinceLastFollowUp: null,
};

vi.mock('../api/services', () => ({
  transactionsApi: {
    getBasic: vi.fn(),
    getAssignments: vi.fn(),
    getFollowUps: vi.fn(),
    getAttachments: vi.fn(),
    getAuditLog: vi.fn(),
  },
  departmentsApi: { getAll: vi.fn() },
}));

function renderDetail(path = '/transactions/1') {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/transactions/:id" element={<TransactionDetailPage />} />
      </Routes>
    </MemoryRouter>,
  );
}

describe('TransactionDetailPage tab loading', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(services.transactionsApi.getBasic).mockResolvedValue({ data: baseTx } as never);
    vi.mocked(services.transactionsApi.getAssignments).mockResolvedValue({ data: [] } as never);
    vi.mocked(services.transactionsApi.getFollowUps).mockResolvedValue({ data: [] } as never);
    vi.mocked(services.departmentsApi.getAll).mockResolvedValue({ data: [] } as never);
  });

  afterEach(() => {
    cleanup();
  });

  it('shows ErrorState when attachments tab fails to load', async () => {
    vi.mocked(services.transactionsApi.getAttachments).mockRejectedValue(new Error('fail'));

    renderDetail('/transactions/1?tab=attachments');

    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent('تعذر تحميل المرفقات');
    });
    expect(screen.getByRole('button', { name: 'إعادة المحاولة' })).toBeInTheDocument();
  });

  it('shows ErrorState when audit tab fails to load', async () => {
    vi.mocked(services.transactionsApi.getAuditLog).mockRejectedValue(new Error('fail'));

    renderDetail('/transactions/1?tab=audit');

    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent('تعذر تحميل سجل التدقيق');
    });
  });

  it('shows ErrorState when timeline tab fails to load', async () => {
    vi.mocked(services.transactionsApi.getAuditLog).mockRejectedValue(new Error('fail'));

    renderDetail('/transactions/1?tab=timeline');

    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent('تعذر تحميل السجل الزمني');
    });
  });

  it('retries attachments tab load from ErrorState', async () => {
    const user = userEvent.setup();
    vi.mocked(services.transactionsApi.getAttachments)
      .mockRejectedValueOnce(new Error('fail'))
      .mockResolvedValueOnce({ data: [] } as never);

    renderDetail('/transactions/1?tab=attachments');

    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'إعادة المحاولة' })).toBeInTheDocument();
    });

    await user.click(screen.getByRole('button', { name: 'إعادة المحاولة' }));

    await waitFor(() => {
      expect(screen.getByText('لا توجد مرفقات')).toBeInTheDocument();
    });
  });
});
