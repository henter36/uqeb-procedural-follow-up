import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
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

const sampleAssignment = {
  id: 10,
  departmentId: 1,
  departmentName: 'إدارة اختبار',
  requiredAction: 'مراجعة',
  dueDate: '2026-02-01',
  replyStatus: 'Pending',
  requiresReply: true,
  status: 'Open',
  isOverdue: false,
};

const sampleFollowUp = {
  id: 20,
  followUpNumber: 'F-1',
  followUpDate: '2026-01-15',
  notes: 'ملاحظة',
  departments: [{ departmentId: 1, departmentName: 'إدارة اختبار' }],
  replyStatus: 'Pending',
  requiresReply: false,
};

const sampleAttachment = {
  id: 30,
  originalFileName: 'file.pdf',
  fileSize: 2048,
  uploadedByName: 'مختبر',
  uploadedAt: '2026-01-10',
  contentType: 'application/pdf',
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
    downloadAttachment: vi.fn(),
  },
  departmentsApi: { getAll: vi.fn() },
  categoriesApi: { getAll: vi.fn() },
  externalPartiesApi: { getAll: vi.fn() },
}));

function getActionBar() {
  return screen.getByRole('navigation', { name: 'إجراءات المعاملة' });
}

function getActionBarButton(name: string) {
  return within(getActionBar()).getByRole('button', { name });
}

function renderDetail(path = '/transactions/1') {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/transactions/:id" element={<TransactionDetailPage />} />
      </Routes>
    </MemoryRouter>,
  );
}

function setupDefaultMocks() {
  vi.mocked(services.transactionsApi.getBasic).mockResolvedValue({ data: baseTx } as never);
  vi.mocked(services.transactionsApi.getAssignments).mockResolvedValue({ data: [sampleAssignment] } as never);
  vi.mocked(services.transactionsApi.getFollowUps).mockResolvedValue({ data: [sampleFollowUp] } as never);
  vi.mocked(services.transactionsApi.getAttachments).mockResolvedValue({ data: [sampleAttachment] } as never);
  vi.mocked(services.transactionsApi.getAuditLog).mockResolvedValue({
    data: { items: [{ id: 1, action: 'Create', userName: 'مختبر', createdAt: '2026-01-01' }], hasNextPage: false },
  } as never);
}

describe('TransactionDetailPage three-tab layout', () => {
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
    setupDefaultMocks();
  });

  afterEach(() => {
    cleanup();
  });

  it('selects details tab by default', async () => {
    renderDetail();

    await waitFor(() => {
      expect(screen.getByRole('tab', { name: 'تفاصيل المعاملة' })).toHaveAttribute('aria-selected', 'true');
    });
  });

  it('shows transaction info, assignments, follow-ups, and attachments without extra clicks', async () => {
    renderDetail();

    await waitFor(() => {
      expect(screen.getByRole('region', { name: 'معلومات المعاملة' })).toBeInTheDocument();
      expect(screen.getByRole('region', { name: 'التحويلات والردود' })).toBeInTheDocument();
      expect(screen.getByRole('region', { name: 'التعقيبات والردود' })).toBeInTheDocument();
      expect(screen.getByRole('region', { name: 'المرفقات' })).toBeInTheDocument();
    });

    expect(screen.getByText('TRK-1')).toBeInTheDocument();
    expect(screen.getByText('مراجعة')).toBeInTheDocument();
    expect(screen.getByText('F-1')).toBeInTheDocument();
    expect(screen.getByText('file.pdf')).toBeInTheDocument();
  });

  it('does not expose legacy sub-tabs', async () => {
    renderDetail();

    await waitFor(() => expect(screen.getByRole('tab', { name: 'تفاصيل المعاملة' })).toBeInTheDocument());

    expect(screen.queryByRole('tab', { name: /نظرة عامة/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('tab', { name: /^التحويلات/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('tab', { name: /^التعقيبات/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('tab', { name: /^المرفقات/ })).not.toBeInTheDocument();
  });

  it('exposes independent timeline and audit tabs', async () => {
    renderDetail();

    await waitFor(() => {
      expect(screen.getByRole('tab', { name: 'الخط الزمني' })).toBeInTheDocument();
      expect(screen.getByRole('tab', { name: 'سجل التدقيق' })).toBeInTheDocument();
    });
  });

  it('loads core data in parallel on first open', async () => {
    renderDetail();

    await waitFor(() => {
      expect(services.transactionsApi.getBasic).toHaveBeenCalled();
      expect(services.transactionsApi.getAssignments).toHaveBeenCalled();
      expect(services.transactionsApi.getFollowUps).toHaveBeenCalled();
      expect(services.transactionsApi.getAttachments).toHaveBeenCalled();
    });
    expect(services.transactionsApi.getAuditLog).not.toHaveBeenCalled();
  });

  it('loads timeline only after opening its tab', async () => {
    const user = userEvent.setup();
    renderDetail();

    await waitFor(() => expect(screen.getByRole('tab', { name: 'الخط الزمني' })).toBeInTheDocument());
    expect(services.transactionsApi.getAuditLog).not.toHaveBeenCalled();

    await user.click(screen.getByRole('tab', { name: 'الخط الزمني' }));

    await waitFor(() => expect(services.transactionsApi.getAuditLog).toHaveBeenCalledTimes(1));
    expect(screen.getByRole('heading', { name: 'الخط الزمني' })).toBeInTheDocument();
  });

  it('loads audit log only after opening its tab', async () => {
    const user = userEvent.setup();
    renderDetail();

    await waitFor(() => expect(screen.getByRole('tab', { name: 'سجل التدقيق' })).toBeInTheDocument());
    expect(services.transactionsApi.getAuditLog).not.toHaveBeenCalled();

    await user.click(screen.getByRole('tab', { name: 'سجل التدقيق' }));

    await waitFor(() => expect(services.transactionsApi.getAuditLog).toHaveBeenCalledTimes(1));
    expect(screen.getByRole('heading', { name: 'سجل التدقيق' })).toBeInTheDocument();
  });

  it.each([
    ['overview'],
    ['assignments'],
    ['followups'],
    ['attachments'],
  ])('maps legacy tab=%s to details tab', async (legacyTab) => {
    renderDetail(`/transactions/1?tab=${legacyTab}`);

    await waitFor(() => {
      expect(screen.getByRole('tab', { name: 'تفاصيل المعاملة' })).toHaveAttribute('aria-selected', 'true');
      expect(screen.getByRole('region', { name: 'التحويلات والردود' })).toBeInTheDocument();
    });
    expect(services.transactionsApi.getAttachments).toHaveBeenCalled();
    expect(services.transactionsApi.getAuditLog).not.toHaveBeenCalled();
  });

  it('opens timeline tab from legacy timeline query', async () => {
    renderDetail('/transactions/1?tab=timeline');

    await waitFor(() => {
      expect(screen.getByRole('tab', { name: 'الخط الزمني' })).toHaveAttribute('aria-selected', 'true');
      expect(services.transactionsApi.getAuditLog).toHaveBeenCalled();
    });
  });

  it('refreshes only assignments card after adding assignment', async () => {
    const user = userEvent.setup();
    vi.mocked(services.transactionsApi.addAssignment).mockResolvedValue({ data: { id: 99 } } as never);
    vi.mocked(services.transactionsApi.getAssignments)
      .mockResolvedValueOnce({ data: [] } as never)
      .mockResolvedValueOnce({ data: [sampleAssignment] } as never);

    renderDetail();
    await waitFor(() => expect(getActionBarButton('إضافة تحويل')).toBeInTheDocument());

    const callsBefore = vi.mocked(services.transactionsApi.getFollowUps).mock.calls.length;
    await user.click(getActionBarButton('إضافة تحويل'));
    await user.selectOptions(screen.getByLabelText('الإدارة *'), '1');
    await user.click(screen.getByRole('button', { name: 'حفظ التحويل' }));

    await waitFor(() => {
      expect(screen.getByRole('status')).toHaveTextContent('تم إضافة التحويل بنجاح');
      expect(screen.getByText('مراجعة')).toBeInTheDocument();
    });
    expect(vi.mocked(services.transactionsApi.getAssignments).mock.calls.length).toBeGreaterThan(1);
    expect(vi.mocked(services.transactionsApi.getFollowUps).mock.calls.length).toBe(callsBefore);
    expect(screen.getByRole('heading', { level: 2, name: 'IN-1' })).toBeInTheDocument();
  });

  it('shows attachments card error with retry', async () => {
    const user = userEvent.setup();
    vi.mocked(services.transactionsApi.getAttachments)
      .mockRejectedValueOnce(new Error('fail'))
      .mockResolvedValueOnce({ data: [] } as never);

    renderDetail();

    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent('تعذر تحميل المرفقات');
    });

    const attachmentsCard = screen.getByRole('region', { name: 'المرفقات' });
    await user.click(within(attachmentsCard).getByRole('button', { name: 'إعادة المحاولة' }));

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
    setupDefaultMocks();
    vi.mocked(services.transactionsApi.getAssignments).mockResolvedValue({ data: [] } as never);
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
    expect(getActionBarButton('إضافة تحويل')).toBeInTheDocument();
  });

  it('opens inline assignment form from action bar', async () => {
    const user = userEvent.setup();
    renderDetail();

    await waitFor(() => {
      expect(getActionBarButton('إضافة تحويل')).toBeInTheDocument();
    });

    await user.click(getActionBarButton('إضافة تحويل'));

    expect(screen.getByRole('region', { name: 'إضافة تحويل' })).toBeInTheDocument();
    expect(screen.getByLabelText('الإدارة *')).toBeInTheDocument();
  });

  it('does not confirm after successful assignment save', async () => {
    const user = userEvent.setup();
    const confirmSpy = vi.spyOn(globalThis, 'confirm');
    vi.mocked(services.transactionsApi.addAssignment).mockResolvedValue({ data: { id: 9 } } as never);

    renderDetail();
    await waitFor(() => expect(getActionBarButton('إضافة تحويل')).toBeInTheDocument());

    await user.click(getActionBarButton('إضافة تحويل'));
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
    setupDefaultMocks();
  });

  afterEach(() => {
    cleanup();
  });

  it('hides mutation actions for reader role across cards and action bar', async () => {
    renderDetail();

    await waitFor(() => expect(screen.getByRole('heading', { level: 2, name: 'IN-1' })).toBeInTheDocument());
    expect(screen.queryByRole('button', { name: 'إضافة تحويل' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'إضافة تعقيب' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'رفع ملف' })).not.toBeInTheDocument();
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
