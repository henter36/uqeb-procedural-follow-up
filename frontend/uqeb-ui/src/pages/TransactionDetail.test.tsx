import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import TransactionDetailPage from './TransactionDetail';
import * as services from '../api/services';

const mockUseAuth = vi.fn(() => ({
  canEdit: true,
  canClose: true,
  isDepartmentUser: false,
  user: { fullName: 'مختبر', role: 'Admin' },
  logout: vi.fn(),
  login: vi.fn(),
  isAdmin: true,
}));

vi.mock('../context/AuthContext', () => ({
  useAuth: () => mockUseAuth(),
}));

vi.mock('../hooks/useReferenceData', () => ({
  useReferenceData: () => ({
    departments: [{ id: 1, name: 'إدارة اختبار', isActive: true }],
    categories: [],
    parties: [],
    loading: false,
    error: null,
    refresh: vi.fn(),
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
    addAssignment: vi.fn(),
    addFollowUp: vi.fn(),
    uploadAttachment: vi.fn(),
    replyAssignment: vi.fn(),
    replyFollowUp: vi.fn(),
    completeResponse: vi.fn(),
    getFollowUpDepartments: vi.fn(),
  },
  departmentsApi: { getAll: vi.fn() },
  categoriesApi: { getAll: vi.fn() },
  externalPartiesApi: { getAll: vi.fn() },
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
    mockUseAuth.mockReturnValue({
      canEdit: true,
      canClose: true,
      isDepartmentUser: false,
      user: { fullName: 'مختبر', role: 'Admin' },
      logout: vi.fn(),
      login: vi.fn(),
      isAdmin: true,
    });
    vi.mocked(services.transactionsApi.getBasic).mockResolvedValue({ data: baseTx } as never);
    vi.mocked(services.transactionsApi.getAssignments).mockResolvedValue({ data: [] } as never);
    vi.mocked(services.transactionsApi.getFollowUps).mockResolvedValue({ data: [] } as never);
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

describe('TransactionDetailPage operational workspace', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseAuth.mockReturnValue({
      canEdit: true,
      canClose: true,
      isDepartmentUser: false,
      user: { fullName: 'مختبر', role: 'Admin' },
      logout: vi.fn(),
      login: vi.fn(),
      isAdmin: true,
    });
    vi.mocked(services.transactionsApi.getBasic).mockResolvedValue({ data: baseTx } as never);
    vi.mocked(services.transactionsApi.getAssignments).mockResolvedValue({ data: [] } as never);
    vi.mocked(services.transactionsApi.getFollowUps).mockResolvedValue({ data: [] } as never);
    vi.mocked(services.transactionsApi.getAttachments).mockResolvedValue({ data: [] } as never);
    vi.mocked(services.transactionsApi.getAuditLog).mockResolvedValue({
      data: { items: [], hasNextPage: false },
    } as never);
  });

  afterEach(() => {
    cleanup();
  });

  it('loads transaction data automatically and shows workspace header', async () => {
    renderDetail();

    await waitFor(() => {
      expect(screen.getByRole('heading', { level: 2, name: 'IN-1' })).toBeInTheDocument();
    });
    expect(screen.getByRole('navigation', { name: 'إجراءات المعاملة' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'إضافة تحويل' })).toBeInTheDocument();
    expect(services.transactionsApi.getBasic).toHaveBeenCalled();
    expect(services.transactionsApi.getAssignments).toHaveBeenCalled();
    expect(services.transactionsApi.getFollowUps).toHaveBeenCalled();
  });

  it('opens inline assignment form from action bar', async () => {
    const user = userEvent.setup();
    renderDetail();

    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'إضافة تحويل' })).toBeInTheDocument();
    });

    await user.click(screen.getByRole('button', { name: 'إضافة تحويل' }));

    expect(screen.getByRole('region', { name: 'إضافة تحويل' })).toBeInTheDocument();
    expect(screen.getByLabelText('الإدارة *')).toBeInTheDocument();
  });

  it('submits assignment without full page reload', async () => {
    const user = userEvent.setup();
    vi.mocked(services.transactionsApi.addAssignment).mockResolvedValue({ data: { id: 9 } } as never);

    renderDetail();

    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'إضافة تحويل' })).toBeInTheDocument();
    });

    await user.click(screen.getByRole('button', { name: 'إضافة تحويل' }));
    await user.selectOptions(screen.getByLabelText('الإدارة *'), '1');
    await user.click(screen.getByRole('button', { name: 'حفظ التحويل' }));

    await waitFor(() => {
      expect(services.transactionsApi.addAssignment).toHaveBeenCalled();
      expect(screen.getByRole('status')).toHaveTextContent('تم إضافة التحويل بنجاح');
    });
  });

  it('shows confirm once when closing dirty assignment form', async () => {
    const user = userEvent.setup();
    const confirmSpy = vi.spyOn(globalThis, 'confirm').mockReturnValue(false);

    renderDetail();
    await waitFor(() => expect(screen.getByRole('button', { name: 'إضافة تحويل' })).toBeInTheDocument());

    await user.click(screen.getByRole('button', { name: 'إضافة تحويل' }));
    await user.selectOptions(screen.getByLabelText('الإدارة *'), '1');
    await user.click(screen.getByRole('button', { name: 'إغلاق النموذج' }));

    expect(confirmSpy).toHaveBeenCalledTimes(1);
    expect(screen.getByRole('region', { name: 'إضافة تحويل' })).toBeInTheDocument();
  });

  it('does not confirm after successful assignment save', async () => {
    const user = userEvent.setup();
    const confirmSpy = vi.spyOn(globalThis, 'confirm');
    vi.mocked(services.transactionsApi.addAssignment).mockResolvedValue({ data: { id: 9 } } as never);

    renderDetail();
    await waitFor(() => expect(screen.getByRole('button', { name: 'إضافة تحويل' })).toBeInTheDocument());

    await user.click(screen.getByRole('button', { name: 'إضافة تحويل' }));
    await user.selectOptions(screen.getByLabelText('الإدارة *'), '1');
    await user.type(screen.getByLabelText('الإجراء المطلوب'), 'مراجعة');
    await user.click(screen.getByRole('button', { name: 'حفظ التحويل' }));

    await waitFor(() => {
      expect(screen.getByRole('status')).toHaveTextContent('تم إضافة التحويل بنجاح');
      expect(screen.queryByRole('region', { name: 'إضافة تحويل' })).not.toBeInTheDocument();
    });
    expect(confirmSpy).not.toHaveBeenCalled();
    expect(services.transactionsApi.addAssignment).toHaveBeenCalledTimes(1);
  });

  it('keeps success message when a secondary refresh fails', async () => {
    const user = userEvent.setup();
    vi.mocked(services.transactionsApi.addAssignment).mockResolvedValue({ data: { id: 9 } } as never);
    let assignmentCalls = 0;
    vi.mocked(services.transactionsApi.getAssignments).mockImplementation(async () => {
      assignmentCalls += 1;
      if (assignmentCalls === 2) {
        throw new Error('refresh fail');
      }
      return { data: [] } as never;
    });

    renderDetail();
    await waitFor(() => expect(screen.getByRole('button', { name: 'إضافة تحويل' })).toBeInTheDocument());

    await user.click(screen.getByRole('button', { name: 'إضافة تحويل' }));
    await user.selectOptions(screen.getByLabelText('الإدارة *'), '1');
    await user.click(screen.getByRole('button', { name: 'حفظ التحويل' }));

    await waitFor(() => {
      expect(screen.getByRole('status')).toHaveTextContent('تم إضافة التحويل بنجاح');
    });
    expect(screen.getByRole('heading', { level: 2, name: 'IN-1' })).toBeInTheDocument();
  });
});

describe('TransactionDetailPage permissions', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseAuth.mockReturnValue({
      canEdit: false,
      canClose: false,
      isDepartmentUser: false,
      user: { fullName: 'قارئ', role: 'Reader' },
      logout: vi.fn(),
      login: vi.fn(),
      isAdmin: false,
    });
    vi.mocked(services.transactionsApi.getBasic).mockResolvedValue({ data: baseTx } as never);
    vi.mocked(services.transactionsApi.getAssignments).mockResolvedValue({ data: [] } as never);
    vi.mocked(services.transactionsApi.getFollowUps).mockResolvedValue({ data: [] } as never);
  });

  afterEach(() => {
    cleanup();
  });

  it('hides mutation actions for reader role', async () => {
    renderDetail();

    await waitFor(() => expect(screen.getByRole('heading', { level: 2, name: 'IN-1' })).toBeInTheDocument());
    expect(screen.queryByRole('button', { name: 'إضافة تحويل' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'إضافة تعقيب' })).not.toBeInTheDocument();
  });

  it('hides admin mutation actions for department user', async () => {
    mockUseAuth.mockReturnValue({
      canEdit: true,
      canClose: false,
      isDepartmentUser: true,
      user: { fullName: 'موظف', role: 'DepartmentUser' },
      logout: vi.fn(),
      login: vi.fn(),
      isAdmin: false,
    });

    renderDetail();

    await waitFor(() => expect(screen.getByRole('heading', { level: 2, name: 'IN-1' })).toBeInTheDocument());
    expect(screen.queryByRole('button', { name: 'إضافة تحويل' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'تعديل' })).not.toBeInTheDocument();
  });
});
