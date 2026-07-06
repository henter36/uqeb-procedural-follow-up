import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import TransactionDetailPage from './TransactionDetail';
import * as services from '../api/services';

type MockAuthState = {
  canEdit: boolean;
  canClose: boolean;
  isDepartmentUser: boolean;
  user: {
    fullName: string;
    role: string;
    departmentId?: number;
  };
  logout: ReturnType<typeof vi.fn>;
  login: ReturnType<typeof vi.fn>;
  isAdmin: boolean;
};

const mockUseAuth = vi.fn<() => MockAuthState>(() => ({
  canEdit: true,
  canClose: true,
  isDepartmentUser: false,
  user: { fullName: 'مختبر', role: 'Admin' },
  logout: vi.fn(),
  login: vi.fn(),
  isAdmin: true,
}));

vi.mock('../context/useAuth', () => ({
  useAuth: () => mockUseAuth(),
}));

vi.mock('../hooks/useReferenceData', () => ({
  useReferenceData: () => ({
    departments: [
      { id: 1, name: 'إدارة اختبار', isActive: true },
      { id: 2, name: 'إدارة ثانية', isActive: true },
    ],
    categories: [],
    parties: [],
    loading: false,
    error: null,
    refresh: vi.fn(),
  }),
}));

vi.mock('../features/scanner/ScanAttachmentButton', () => ({
  default: ({
    onSaveScannedFile,
    onSaved,
    beforeOpen,
    disabled,
  }: {
    onSaveScannedFile?: (file: File) => Promise<void>;
    onSaved: () => void;
    beforeOpen?: () => Promise<boolean>;
    disabled?: boolean;
  }) => (
    <button
      type="button"
      disabled={disabled}
      onClick={async () => {
        if (beforeOpen && !await beforeOpen()) return;
        await onSaveScannedFile?.(new File(['scan'], 'scan.jpg', { type: 'image/jpeg' }));
        onSaved();
      }}
    >
      مسح ضوئي
    </button>
  ),
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
  completionDate: null,
  completionDays: null,
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
  requiresReply: true,
};

const sampleAttachment = {
  id: 30,
  originalFileName: 'file.pdf',
  fileSize: 2048,
  uploadedByName: 'مختبر',
  uploadedAt: '2026-01-10',
  contentType: 'application/pdf',
};

const defaultAllowedActions = {
  canEdit: true,
  canClose: true,
  isDepartmentUser: false,
  canRegisterResponse: false,
  canShowClose: true,
  showMutationActions: true,
  canReply: true,
  hasPendingDepartments: false,
};

const defaultTemporalFacts = {
  isOpen: true,
  isResponseOverdue: false,
  isOverdue: false,
  ageDays: 1,
  daysOverdue: null,
  completionDays: null,
};

const defaultWorkspace = {
  transaction: baseTx,
  assignments: [sampleAssignment],
  followUps: [sampleFollowUp],
  attachments: [sampleAttachment],
  temporalFacts: defaultTemporalFacts,
  allowedActions: defaultAllowedActions,
};

vi.mock('../api/services', () => ({
  transactionsApi: {
    getBasic: vi.fn(),
    getWorkspace: vi.fn(),
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
    editResponse: vi.fn(),
    adminEditAssignment: vi.fn(),
    adminEditTransactionDates: vi.fn(),
    getFollowUpDepartments: vi.fn(),
    downloadAttachment: vi.fn(),
    close: vi.fn(),
    enableRecurring: vi.fn(),
  },
  departmentResponsesApi: {
    getDepartmentTransactions: vi.fn(),
    getById: vi.fn(),
    create: vi.fn(),
    update: vi.fn(),
    adminEdit: vi.fn(),
    submit: vi.fn(),
    uploadAttachment: vi.fn(),
  },
  recurringTemplatesApi: {
    getById: vi.fn(),
  },
  departmentsApi: { getAll: vi.fn() },
  categoriesApi: { getAll: vi.fn() },
  externalPartiesApi: { getAll: vi.fn() },
}));

type MockedApiFunction = {
  mockResolvedValue(value: unknown): MockedApiFunction;
  mockResolvedValueOnce(value: unknown): MockedApiFunction;
  mockRejectedValue(value: unknown): MockedApiFunction;
  mockRejectedValueOnce(value: unknown): MockedApiFunction;
  mockReturnValue(value: unknown): MockedApiFunction;
  mock: {
    calls: unknown[][];
  };
};

const mockApi = (fn: unknown) => fn as MockedApiFunction;

function getActionBar() {
  return screen.getByRole('navigation', { name: 'إجراءات المعاملة' });
}

function getActionBarButton(name: string) {
  return within(getActionBar()).getByRole('button', { name });
}

function getAssignmentsCard() {
  return screen.getByRole('region', { name: 'الاحالات والردود' });
}

function getFollowUpsCard() {
  return screen.getByRole('region', { name: 'التعقيبات والردود' });
}

function getAttachmentsCard() {
  return screen.getByRole('region', { name: 'المرفقات' });
}

function getResponseCard() {
  return screen.getByRole('region', { name: 'الإفادة' });
}

function getCurrentStatusCard() {
  return screen.getByTestId('current-action-status-card');
}

async function waitForDetailsReady() {
  await waitFor(() => {
    expect(screen.getByRole('region', { name: 'معلومات المعاملة' })).toBeInTheDocument();
    expect(getAssignmentsCard()).toBeInTheDocument();
  });
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
  mockApi(services.transactionsApi.getWorkspace).mockResolvedValue({ data: defaultWorkspace });
  mockApi(services.transactionsApi.getBasic).mockResolvedValue({ data: baseTx });
  mockApi(services.transactionsApi.getAssignments).mockResolvedValue({ data: [sampleAssignment] });
  mockApi(services.transactionsApi.getFollowUps).mockResolvedValue({ data: [sampleFollowUp] });
  mockApi(services.transactionsApi.getAttachments).mockResolvedValue({ data: [sampleAttachment] });
  mockApi(services.transactionsApi.getAuditLog).mockResolvedValue({
    data: { items: [{ id: 1, action: 'Create', userName: 'مختبر', createdAt: '2026-01-01' }], hasNextPage: false },
  });
  mockApi(services.transactionsApi.getFollowUpDepartments).mockResolvedValue({
    data: [{ departmentId: 2, departmentName: 'إدارة ثانية', isDefaultSelected: true }],
  });
  mockApi(services.departmentResponsesApi.getDepartmentTransactions).mockResolvedValue({ data: [] });
  mockApi(services.departmentResponsesApi.getById).mockResolvedValue({
    data: {
      id: 100,
      transactionId: 1,
      transactionSubject: 'موضوع',
      internalTrackingNumber: 'TRK-1',
      departmentId: 1,
      departmentName: 'إدارة اختبار',
      responseText: 'نص الإفادة',
      status: 'Draft',
      submittedByName: 'موظف إدارة',
      createdAt: '2026-01-01',
      attachments: [],
    },
  });
  mockApi(services.departmentResponsesApi.create).mockResolvedValue({
    data: {
      id: 100,
      transactionId: 1,
      transactionSubject: 'موضوع',
      internalTrackingNumber: 'TRK-1',
      departmentId: 1,
      departmentName: 'إدارة اختبار',
      responseText: 'نص الإفادة',
      status: 'Draft',
      submittedByName: 'موظف إدارة',
      createdAt: '2026-01-01',
      attachments: [],
    },
  });
  mockApi(services.departmentResponsesApi.update).mockResolvedValue({
    data: {
      id: 100,
      transactionId: 1,
      transactionSubject: 'موضوع',
      internalTrackingNumber: 'TRK-1',
      departmentId: 1,
      departmentName: 'إدارة اختبار',
      responseText: 'نص الإفادة',
      status: 'Draft',
      submittedByName: 'موظف إدارة',
      createdAt: '2026-01-01',
      attachments: [],
    },
  });
  mockApi(services.departmentResponsesApi.adminEdit).mockResolvedValue({
    data: {
      id: 100,
      transactionId: 1,
      transactionSubject: 'موضوع',
      internalTrackingNumber: 'TRK-1',
      departmentId: 1,
      departmentName: 'إدارة اختبار',
      responseText: 'نص الإفادة',
      status: 'Approved',
      submittedByName: 'موظف إدارة',
      submittedAt: '2026-01-05',
      createdAt: '2026-01-01',
      attachments: [],
    },
  });
  mockApi(services.transactionsApi.adminEditAssignment).mockResolvedValue({
    data: {
      ...sampleAssignment,
      assignedDate: '2026-01-01',
    },
  });
  mockApi(services.departmentResponsesApi.uploadAttachment).mockResolvedValue({
    data: {
      id: 101,
      originalFileName: 'scan.jpg',
      fileSizeBytes: 4,
      uploadedByName: 'موظف إدارة',
      uploadedAt: '2026-01-01',
    },
  });
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
    vi.restoreAllMocks();
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
      expect(getAssignmentsCard()).toBeInTheDocument();
      expect(getFollowUpsCard()).toBeInTheDocument();
      expect(getAttachmentsCard()).toBeInTheDocument();
    });

    expect(screen.getByText('TRK-1')).toBeInTheDocument();
    expect(screen.getByText('مراجعة')).toBeInTheDocument();
    expect(screen.getByRole('columnheader', { name: 'تاريخ إنجاز الإدارة' })).toBeInTheDocument();
    const assignmentsCard = getAssignmentsCard();
    expect(within(assignmentsCard).getByText('عمر المعاملة: 1 يوم')).toBeInTheDocument();
    expect(within(assignmentsCard).getAllByText(/عمر المعاملة:/)).toHaveLength(1);
    expect(within(assignmentsCard).getByText('عمر المعاملة: 1 يوم').closest('.transaction-metric-tile')).toBeNull();
    expect(screen.getByText('F-1')).toBeInTheDocument();
    expect(screen.getByText('file.pdf')).toBeInTheDocument();
  });

  it('shows explicit edit response action for authorized admin when response exists', async () => {
    mockApi(services.transactionsApi.getWorkspace).mockResolvedValue({
      data: {
        ...defaultWorkspace,
        assignments: [{
          ...sampleAssignment,
          replyStatus: 'Replied',
          status: 'Completed',
          departmentResponseId: 100,
          responseDate: '2026-01-05',
          canAdminEdit: true,
        }],
      },
    });

    renderDetail();
    await waitForDetailsReady();

    const card = getAssignmentsCard();
    expect(within(card).getByRole('button', { name: 'تعديل الإفادة' })).toBeInTheDocument();
  });

  it('does not expose legacy sub-tabs', async () => {
    renderDetail();

    await waitFor(() => expect(screen.getByRole('tab', { name: 'تفاصيل المعاملة' })).toBeInTheDocument());

    expect(screen.queryByRole('tab', { name: /نظرة عامة/ })).not.toBeInTheDocument();
    expect(screen.queryByRole('tab', { name: /^الاحالةات/ })).not.toBeInTheDocument();
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

  it('loads workspace on first open', async () => {
    renderDetail();

    await waitFor(() => {
      expect(services.transactionsApi.getWorkspace).toHaveBeenCalled();
    });
    expect(services.transactionsApi.getBasic).not.toHaveBeenCalled();
    expect(services.transactionsApi.getAssignments).not.toHaveBeenCalled();
    expect(services.transactionsApi.getFollowUps).not.toHaveBeenCalled();
    expect(services.transactionsApi.getAttachments).not.toHaveBeenCalled();
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
      expect(getAssignmentsCard()).toBeInTheDocument();
    });
    expect(services.transactionsApi.getWorkspace).toHaveBeenCalled();
    expect(services.transactionsApi.getAuditLog).not.toHaveBeenCalled();
  });

  it('opens timeline tab from legacy timeline query', async () => {
    renderDetail('/transactions/1?tab=timeline');

    await waitFor(() => {
      expect(screen.getByRole('tab', { name: 'الخط الزمني' })).toHaveAttribute('aria-selected', 'true');
      expect(services.transactionsApi.getAuditLog).toHaveBeenCalled();
    });
  });

  it('refreshes workspace after adding assignment from card button', async () => {
    const user = userEvent.setup();
    mockApi(services.transactionsApi.addAssignment).mockResolvedValue({ data: { id: 99 } });
    mockApi(services.transactionsApi.getWorkspace)
      .mockResolvedValueOnce({ data: { ...defaultWorkspace, assignments: [] } })
      .mockResolvedValueOnce({ data: defaultWorkspace });

    renderDetail();
    await waitForDetailsReady();
    const card = getAssignmentsCard();
    await waitFor(() => expect(within(card).getByRole('button', { name: '+ إضافة احالة' })).toBeInTheDocument());

    await user.click(within(card).getByRole('button', { name: '+ إضافة احالة' }));
    expect(within(card).getByTestId('assignment-form-panel')).toBeInTheDocument();

    await user.selectOptions(screen.getByLabelText('الإدارة *'), '2');
    await user.type(screen.getByLabelText('تاريخ الإحالة *'), '16/01/1448');
    await user.type(screen.getByLabelText('الإجراء المطلوب'), 'مراجعة');
    await user.click(screen.getByRole('button', { name: 'حفظ الاحالة' }));

    await waitFor(() => {
      expect(screen.getByRole('status')).toHaveTextContent('تم إضافة الاحالة بنجاح');
      expect(screen.getByText('مراجعة')).toBeInTheDocument();
      expect(within(card).queryByTestId('assignment-form-panel')).not.toBeInTheDocument();
    });
    expect(services.transactionsApi.addAssignment).toHaveBeenCalledTimes(1);
    expect(mockApi(services.transactionsApi.getWorkspace).mock.calls.length).toBeGreaterThan(1);
    expect(services.transactionsApi.getFollowUps).not.toHaveBeenCalled();
    expect(screen.getByRole('heading', { level: 2, name: 'IN-1' })).toBeInTheDocument();
  });

  it('shows page error with retry when workspace load fails', async () => {
    const user = userEvent.setup();
    mockApi(services.transactionsApi.getWorkspace)
      .mockRejectedValueOnce(Object.assign(new Error('server error'), {
        isAxiosError: true,
        response: { status: 500 },
        code: 'ERR_BAD_RESPONSE',
      }))
      .mockResolvedValueOnce({ data: defaultWorkspace });

    renderDetail();

    await waitFor(() => {
      expect(screen.getByText('تعذر تحميل المعاملة')).toBeInTheDocument();
    });

    await user.click(screen.getByRole('button', { name: 'إعادة المحاولة' }));

    await waitFor(() => {
      expect(screen.getByRole('region', { name: 'معلومات المعاملة' })).toBeInTheDocument();
    });
  });

});

describe('TransactionDetailPage department user permissions', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseAuth.mockReturnValue({
      canEdit: false,
      canClose: false,
      isDepartmentUser: true,
      user: {
        fullName: 'موظف إدارة',
        role: 'DepartmentUser',
        departmentId: 1,
      },
      logout: vi.fn(),
      login: vi.fn(),
      isAdmin: false,
    });
    setupDefaultMocks();
    mockApi(services.departmentResponsesApi.getDepartmentTransactions).mockResolvedValue({
      data: [{
        transactionId: 1,
        internalTrackingNumber: 'TRK-1',
        subject: 'موضوع',
        priority: 'Normal',
        departmentId: 1,
        departmentName: 'إدارة اختبار',
        canCreateResponse: true,
        canEditResponse: false,
        canSubmitResponse: false,
      }],
    });
    mockApi(services.transactionsApi.getWorkspace).mockResolvedValue({
      data: {
        ...defaultWorkspace,
        transaction: {
          ...baseTx,
          requiresResponse: true,
          responseType: 'Internal',
          responseCompleted: false,
        },
      },
    });
  });

  afterEach(() => {
    vi.restoreAllMocks();
    cleanup();
  });

  it('hides transaction mutation, follow-up, print, and reply actions for department users', async () => {
    renderDetail();
    await waitForDetailsReady();

    const actionBar = getActionBar();
    expect(within(actionBar).queryByRole('button', { name: 'إضافة احالة' })).not.toBeInTheDocument();
    expect(within(actionBar).queryByRole('button', { name: 'إضافة تعقيب' })).not.toBeInTheDocument();
    expect(within(actionBar).queryByRole('button', { name: 'إضافة مرفق' })).not.toBeInTheDocument();
    expect(within(actionBar).queryByRole('link', { name: 'تعديل' })).not.toBeInTheDocument();
    expect(within(actionBar).queryByRole('button', { name: 'خطاب تعقيب PDF' })).not.toBeInTheDocument();
    expect(within(actionBar).queryByRole('button', { name: 'إغلاق المعاملة' })).not.toBeInTheDocument();

    expect(within(getAssignmentsCard()).queryByRole('button', { name: '+ إضافة احالة' })).not.toBeInTheDocument();
    expect(within(getFollowUpsCard()).queryByRole('button', { name: '+ إضافة تعقيب' })).not.toBeInTheDocument();
    expect(within(getAttachmentsCard()).queryByRole('button', { name: '+ إضافة مرفق' })).not.toBeInTheDocument();
    expect(screen.queryAllByRole('button', { name: 'تسجيل رد' })).toHaveLength(0);
  });

  it('opens a clear department response form and scrolls to it', async () => {
    const user = userEvent.setup();
    const scrollIntoView = vi.fn();
    Object.defineProperty(globalThis.Element.prototype, 'scrollIntoView', {
      configurable: true,
      value: scrollIntoView,
    });

    const view = renderDetail();
    await waitForDetailsReady();

    await waitFor(() => expect(within(getActionBar()).getByRole('button', { name: 'تسجيل إفادة' })).toBeInTheDocument());
    await user.click(within(getActionBar()).getByRole('button', { name: 'تسجيل إفادة' }));

    const panel = screen.getByRole('region', { name: 'تسجيل إفادة' });
    expect(panel).toHaveClass('workspace-action-panel--prominent');
    expect(within(panel).getByRole('heading', { name: 'إفادة الإدارة' })).toBeInTheDocument();
    expect(within(panel).getByLabelText('نص الإفادة *')).toHaveAttribute('rows', '4');
    expect(within(panel).getByLabelText('نص الإفادة *')).toHaveFocus();
    expect(within(panel).getByRole('button', { name: 'رفع ملف' })).toBeEnabled();
    expect(within(panel).getByRole('button', { name: 'مسح ضوئي' })).toBeEnabled();
    expect(view.container.querySelector('[role="group"]')).not.toBeInTheDocument();
    expect(scrollIntoView).toHaveBeenCalled();
  });

  it('auto-saves a draft before scanning and uploads scanned files as department response attachments', async () => {
    const user = userEvent.setup();
    renderDetail();
    await waitForDetailsReady();

    await waitFor(() => expect(within(getActionBar()).getByRole('button', { name: 'تسجيل إفادة' })).toBeInTheDocument());
    await user.click(within(getActionBar()).getByRole('button', { name: 'تسجيل إفادة' }));
    await user.type(screen.getByLabelText('نص الإفادة *'), 'نص إفادة الإدارة');

    const panel = screen.getByRole('region', { name: 'تسجيل إفادة' });
    await user.click(within(panel).getByRole('button', { name: 'مسح ضوئي' }));

    await waitFor(() => {
      expect(services.departmentResponsesApi.create).toHaveBeenCalledWith({
        transactionId: 1,
        responseText: 'نص إفادة الإدارة',
      });
      expect(services.departmentResponsesApi.uploadAttachment).toHaveBeenCalledWith(
        100,
        expect.any(File),
      );
    });
    expect(services.transactionsApi.uploadAttachment).not.toHaveBeenCalledWith(1, expect.any(File), 'Scan');
    expect(screen.getByRole('status')).toHaveTextContent('تم رفع مرفق الإفادة من الماسح الضوئي');
  });

  it('auto-saves a draft before uploading a department response attachment', async () => {
    const user = userEvent.setup();
    renderDetail();
    await waitForDetailsReady();

    await waitFor(() => expect(within(getActionBar()).getByRole('button', { name: 'تسجيل إفادة' })).toBeInTheDocument());
    await user.click(within(getActionBar()).getByRole('button', { name: 'تسجيل إفادة' }));
    await user.type(screen.getByLabelText('نص الإفادة *'), 'نص إفادة الإدارة');
    await user.upload(screen.getByLabelText('رفع مرفق إفادة'), new File(['pdf'], 'response.pdf', { type: 'application/pdf' }));

    await waitFor(() => {
      expect(services.departmentResponsesApi.create).toHaveBeenCalledWith({
        transactionId: 1,
        responseText: 'نص إفادة الإدارة',
      });
      expect(services.departmentResponsesApi.uploadAttachment).toHaveBeenCalledWith(100, expect.any(File));
    });
    expect(services.transactionsApi.uploadAttachment).not.toHaveBeenCalledWith(1, expect.any(File), 'Scan');
    expect(screen.getByRole('status')).toHaveTextContent('تم رفع مرفق الإفادة');
  });

  it('shows continue action and correction note for returned department responses', async () => {
    const user = userEvent.setup();
    mockApi(services.departmentResponsesApi.getDepartmentTransactions).mockResolvedValue({
      data: [{
        transactionId: 1,
        internalTrackingNumber: 'TRK-1',
        subject: 'موضوع',
        priority: 'Normal',
        departmentId: 1,
        departmentName: 'إدارة اختبار',
        departmentResponseId: 100,
        departmentResponseStatus: 'ReturnedForCorrection',
        canCreateResponse: false,
        canEditResponse: true,
        canSubmitResponse: true,
      }],
    });
    mockApi(services.departmentResponsesApi.getById).mockResolvedValue({
      data: {
        id: 100,
        transactionId: 1,
        transactionSubject: 'موضوع',
        internalTrackingNumber: 'TRK-1',
        departmentId: 1,
        departmentName: 'إدارة اختبار',
        responseText: 'نص يحتاج تعديل',
        status: 'ReturnedForCorrection',
        submittedByName: 'موظف إدارة',
        reviewNote: 'أضف التفاصيل',
        createdAt: '2026-01-01',
        attachments: [],
      },
    });

    renderDetail();
    await waitForDetailsReady();

    await waitFor(() => expect(within(getActionBar()).getByRole('button', { name: 'استكمال إفادة' })).toBeInTheDocument());
    await user.click(within(getActionBar()).getByRole('button', { name: 'استكمال إفادة' }));

    expect(await screen.findByRole('heading', { name: 'إفادة الإدارة' })).toBeInTheDocument();
    expect(screen.getByText(/أُعيدت الإفادة للتصحيح/)).toBeInTheDocument();
    expect(screen.getByText(/أضف التفاصيل/)).toBeInTheDocument();
  });

  it('shows submitted-for-review status without a new submit button', async () => {
    mockApi(services.departmentResponsesApi.getDepartmentTransactions).mockResolvedValue({
      data: [{
        transactionId: 1,
        internalTrackingNumber: 'TRK-1',
        subject: 'موضوع',
        priority: 'Normal',
        departmentId: 1,
        departmentName: 'إدارة اختبار',
        departmentResponseId: 100,
        departmentResponseStatus: 'SubmittedForReview',
        canCreateResponse: false,
        canEditResponse: false,
        canSubmitResponse: false,
      }],
    });

    renderDetail();
    await waitForDetailsReady();

    await waitFor(() => expect(within(getActionBar()).getByText('بانتظار المراجعة')).toBeInTheDocument());
    expect(within(getActionBar()).queryByRole('button', { name: 'تسجيل إفادة' })).not.toBeInTheDocument();
    expect(within(getActionBar()).queryByRole('button', { name: 'استكمال إفادة' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'إرسال الإفادة' })).not.toBeInTheDocument();
  });

  it('shows department response attachments inside the response form when available', async () => {
    const user = userEvent.setup();
    mockApi(services.departmentResponsesApi.getDepartmentTransactions).mockResolvedValue({
      data: [{
        transactionId: 1,
        internalTrackingNumber: 'TRK-1',
        subject: 'موضوع',
        priority: 'Normal',
        departmentId: 1,
        departmentName: 'إدارة اختبار',
        departmentResponseId: 100,
        departmentResponseStatus: 'Draft',
        canCreateResponse: false,
        canEditResponse: true,
        canSubmitResponse: true,
      }],
    });
    mockApi(services.departmentResponsesApi.getById).mockResolvedValue({
      data: {
        id: 100,
        transactionId: 1,
        transactionSubject: 'موضوع',
        internalTrackingNumber: 'TRK-1',
        departmentId: 1,
        departmentName: 'إدارة اختبار',
        responseText: 'نص مسودة',
        status: 'Draft',
        submittedByName: 'موظف إدارة',
        createdAt: '2026-01-01',
        attachments: [{
          id: 7,
          originalFileName: 'response.pdf',
          fileSizeBytes: 1200,
          uploadedByName: 'موظف إدارة',
          uploadedAt: '2026-01-01',
        }],
      },
    });

    renderDetail();
    await waitForDetailsReady();

    await waitFor(() => expect(within(getActionBar()).getByRole('button', { name: 'استكمال إفادة' })).toBeInTheDocument());
    await user.click(within(getActionBar()).getByRole('button', { name: 'استكمال إفادة' }));

    const attachmentsToolbar = screen.getByText('مرفقات: 1').closest('fieldset');
    if (!attachmentsToolbar) throw new Error('Expected attachment toolbar fieldset');
    expect(within(attachmentsToolbar).getByText('مرفقات: 1')).toBeInTheDocument();
    expect(within(attachmentsToolbar).getByRole('button', { name: 'رفع ملف' })).toBeEnabled();
    expect(within(attachmentsToolbar).getByRole('button', { name: 'مسح ضوئي' })).toBeEnabled();
    expect(within(attachmentsToolbar).getByText('response.pdf')).toBeInTheDocument();
  });

  it('shows error message and retry button when getDepartmentTransactions fails', async () => {
    const user = userEvent.setup();
    mockApi(services.departmentResponsesApi.getDepartmentTransactions)
      .mockRejectedValueOnce(new Error('network error'))
      .mockResolvedValueOnce({
        data: [{
          transactionId: 1,
          internalTrackingNumber: 'TRK-1',
          subject: 'موضوع',
          priority: 'Normal',
          departmentId: 1,
          departmentName: 'إدارة اختبار',
          canCreateResponse: true,
          canEditResponse: false,
          canSubmitResponse: false,
        }],
      });

    renderDetail();
    await waitForDetailsReady();

    await waitFor(() => expect(screen.getByText('تعذر تحميل حالة إفادة الإدارة. أعد المحاولة.')).toBeInTheDocument());
    const retryBtn = screen.getByRole('button', { name: 'إعادة المحاولة' });
    await user.click(retryBtn);

    await waitFor(() => expect(within(getActionBar()).getByRole('button', { name: 'تسجيل إفادة' })).toBeInTheDocument());
    expect(screen.queryByText('تعذر تحميل حالة إفادة الإدارة. أعد المحاولة.')).not.toBeInTheDocument();
  });

  it('submit button is disabled when canSubmitResponse is false', async () => {
    mockApi(services.departmentResponsesApi.getDepartmentTransactions).mockResolvedValue({
      data: [{
        transactionId: 1,
        internalTrackingNumber: 'TRK-1',
        subject: 'موضوع',
        priority: 'Normal',
        departmentId: 1,
        departmentName: 'إدارة اختبار',
        departmentResponseId: 100,
        departmentResponseStatus: 'Draft',
        canCreateResponse: false,
        canEditResponse: true,
        canSubmitResponse: false,
      }],
    });

    const user = userEvent.setup();
    renderDetail();
    await waitForDetailsReady();

    await waitFor(() => expect(within(getActionBar()).getByRole('button', { name: 'استكمال إفادة' })).toBeInTheDocument());
    await user.click(within(getActionBar()).getByRole('button', { name: 'استكمال إفادة' }));

    await waitFor(() => expect(screen.getByRole('heading', { name: 'إفادة الإدارة' })).toBeInTheDocument());
    const submitBtn = screen.getByRole('button', { name: 'إرسال الإفادة' });
    expect(submitBtn).toBeDisabled();
  });

  it('does not call create again on submit retry after saveDraft succeeds but submit fails', async () => {
    const user = userEvent.setup();
    mockApi(services.departmentResponsesApi.submit).mockRejectedValueOnce(new Error('server error'));

    renderDetail();
    await waitForDetailsReady();

    await waitFor(() => expect(within(getActionBar()).getByRole('button', { name: 'تسجيل إفادة' })).toBeInTheDocument());
    await user.click(within(getActionBar()).getByRole('button', { name: 'تسجيل إفادة' }));

    await waitFor(() => expect(screen.getByRole('heading', { name: 'إفادة الإدارة' })).toBeInTheDocument());
    await user.type(screen.getByLabelText('نص الإفادة *'), 'نص الإفادة');
    await user.click(screen.getByRole('button', { name: 'إرسال الإفادة' }));

    await waitFor(() => expect(screen.getByRole('button', { name: 'إرسال الإفادة' })).toBeEnabled());
    expect(services.departmentResponsesApi.create).toHaveBeenCalledTimes(1);

    mockApi(services.departmentResponsesApi.submit).mockResolvedValueOnce({
      data: {
        id: 100,
        transactionId: 1,
        transactionSubject: 'موضوع',
        internalTrackingNumber: 'TRK-1',
        departmentId: 1,
        departmentName: 'إدارة اختبار',
        responseText: 'نص الإفادة',
        status: 'SubmittedForReview',
        submittedByName: 'موظف إدارة',
        createdAt: '2026-01-01',
        attachments: [],
      },
    });
    await user.click(screen.getByRole('button', { name: 'إرسال الإفادة' }));

    await waitFor(() => expect(services.departmentResponsesApi.submit).toHaveBeenCalledTimes(2));
    expect(services.departmentResponsesApi.create).toHaveBeenCalledTimes(1);
  });

  it('keeps mutation actions available for data entry users', async () => {
    mockUseAuth.mockReturnValue({
      canEdit: true,
      canClose: false,
      isDepartmentUser: false,
      user: { fullName: 'مدخل بيانات', role: 'DataEntry' },
      logout: vi.fn(),
      login: vi.fn(),
      isAdmin: false,
    });

    renderDetail();
    await waitForDetailsReady();

    expect(within(getAssignmentsCard()).getByRole('button', { name: '+ إضافة احالة' })).toBeInTheDocument();
    expect(within(getFollowUpsCard()).getByRole('button', { name: '+ إضافة تعقيب' })).toBeInTheDocument();
    expect(within(getActionBar()).getByRole('link', { name: 'تعديل' })).toBeInTheDocument();
    expect(within(getFollowUpsCard()).getByRole('button', { name: 'خطاب تعقيب PDF' })).toBeInTheDocument();
  });
});

describe('TransactionDetailPage card interaction flows', () => {
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
    mockApi(services.transactionsApi.getAssignments).mockResolvedValue({ data: [] });
    mockApi(services.transactionsApi.getFollowUps).mockResolvedValue({ data: [] });
    mockApi(services.transactionsApi.getAttachments).mockResolvedValue({ data: [] });
  });

  afterEach(() => {
    vi.restoreAllMocks();
    cleanup();
  });

  it('opens assignment form inside assignments card from card header button', async () => {
    const user = userEvent.setup();
    renderDetail();
    await waitForDetailsReady();
    const card = getAssignmentsCard();
    await waitFor(() => expect(within(card).getByRole('button', { name: '+ إضافة احالة' })).toBeInTheDocument());

    const addButton = within(card).getByRole('button', { name: '+ إضافة احالة' });
    expect(addButton).toHaveAttribute('type', 'button');

    await user.click(addButton);

    const panel = within(card).getByTestId('assignment-form-panel');
    expect(panel).toBeInTheDocument();
    expect(within(panel).getByLabelText('الإدارة *')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'حفظ الاحالة' })).toBeInTheDocument();
    expect(screen.queryByTestId('followup-form-panel')).not.toBeInTheDocument();
    expect(screen.queryByTestId('attachment-form-panel')).not.toBeInTheDocument();
  });

  it('never prefills a new assignment letter number from the transaction outgoing number or any prior assignment', async () => {
    const user = userEvent.setup();
    mockApi(services.transactionsApi.getWorkspace).mockResolvedValue({
      data: {
        ...defaultWorkspace,
        transaction: {
          ...baseTx,
          outgoingNumber: 'OUT-88',
        },
        assignments: [
          {
            id: 1,
            departmentId: 1,
            departmentName: 'إدارة أ',
            letterNumber: 'LET-001',
            assignedDate: '2026-01-01',
            requiresReply: true,
            replyStatus: 'Pending',
            status: 'Active',
            isOverdue: false,
          },
        ],
      },
    });

    renderDetail();
    await waitForDetailsReady();
    const card = getAssignmentsCard();

    await user.click(within(card).getByRole('button', { name: '+ إضافة احالة' }));

    expect(screen.getByLabelText('رقم الخطاب')).toHaveValue('');
  });

  it('saves assignment from card form and refreshes data', async () => {
    const user = userEvent.setup();
    mockApi(services.transactionsApi.addAssignment).mockResolvedValue({ data: { id: 9 } });
    mockApi(services.transactionsApi.getWorkspace)
      .mockResolvedValueOnce({ data: { ...defaultWorkspace, assignments: [] } })
      .mockResolvedValue({ data: defaultWorkspace });

    renderDetail();
    await waitForDetailsReady();
    const card = getAssignmentsCard();
    await waitFor(() => expect(within(card).getByRole('button', { name: '+ إضافة احالة' })).toBeInTheDocument());

    await user.click(within(card).getByRole('button', { name: '+ إضافة احالة' }));
    await user.selectOptions(screen.getByLabelText('الإدارة *'), '2');
    await user.type(screen.getByLabelText('تاريخ الإحالة *'), '16/01/1448');
    await user.type(screen.getByLabelText('الإجراء المطلوب'), 'مراجعة');
    await user.click(screen.getByRole('button', { name: 'حفظ الاحالة' }));

    await waitFor(() => {
      expect(screen.getByRole('status')).toHaveTextContent('تم إضافة الاحالة بنجاح');
      expect(within(card).queryByTestId('assignment-form-panel')).not.toBeInTheDocument();
    });
    expect(services.transactionsApi.addAssignment).toHaveBeenCalledTimes(1);
    expect(mockApi(services.transactionsApi.getWorkspace).mock.calls.length).toBeGreaterThan(1);
  });

  it('keeps assignment form open when save fails', async () => {
    const user = userEvent.setup();
    mockApi(services.transactionsApi.addAssignment).mockRejectedValue(new Error('save failed'));

    renderDetail();
    await waitForDetailsReady();
    const card = getAssignmentsCard();
    await waitFor(() => expect(within(card).getByRole('button', { name: '+ إضافة احالة' })).toBeInTheDocument());

    await user.click(within(card).getByRole('button', { name: '+ إضافة احالة' }));
    await user.selectOptions(screen.getByLabelText('الإدارة *'), '2');
    await user.type(screen.getByLabelText('تاريخ الإحالة *'), '16/01/1448');
    await user.click(screen.getByRole('button', { name: 'حفظ الاحالة' }));

    await waitFor(() => expect(screen.getByRole('alert')).toBeInTheDocument());
    expect(within(card).getByTestId('assignment-form-panel')).toBeInTheDocument();
    expect(services.transactionsApi.addAssignment).toHaveBeenCalledTimes(1);
  });

  it('toggles assignment form open and closed without confirm when clean', async () => {
    const user = userEvent.setup();
    const confirmSpy = vi.spyOn(globalThis, 'confirm');

    renderDetail();
    await waitForDetailsReady();
    const card = getAssignmentsCard();
    await waitFor(() => expect(within(card).getByRole('button', { name: '+ إضافة احالة' })).toBeInTheDocument());

    const toggle = within(card).getByRole('button', { name: '+ إضافة احالة' });
    await user.click(toggle);
    expect(within(card).getByTestId('assignment-form-panel')).toBeInTheDocument();

    await user.click(toggle);
    expect(within(card).queryByTestId('assignment-form-panel')).not.toBeInTheDocument();
    expect(confirmSpy).not.toHaveBeenCalled();
  });

  it('confirms once when closing dirty assignment form via toggle', async () => {
    const user = userEvent.setup();
    const confirmSpy = vi.spyOn(globalThis, 'confirm').mockReturnValue(true);

    renderDetail();
    await waitForDetailsReady();
    const card = getAssignmentsCard();
    await waitFor(() => expect(within(card).getByRole('button', { name: '+ إضافة احالة' })).toBeInTheDocument());

    await user.click(within(card).getByRole('button', { name: '+ إضافة احالة' }));
    await user.type(screen.getByLabelText('الإجراء المطلوب'), 'تعديل');
    await user.click(within(card).getByRole('button', { name: '+ إضافة احالة' }));

    expect(confirmSpy).toHaveBeenCalledTimes(1);
    expect(within(card).queryByTestId('assignment-form-panel')).not.toBeInTheDocument();
  });

  it('closes assignment form from panel header without confirm after successful save', async () => {
    const user = userEvent.setup();
    const confirmSpy = vi.spyOn(globalThis, 'confirm');
    mockApi(services.transactionsApi.addAssignment).mockResolvedValue({ data: { id: 9 } });

    renderDetail();
    await waitForDetailsReady();
    const card = getAssignmentsCard();
    await waitFor(() => expect(within(card).getByRole('button', { name: '+ إضافة احالة' })).toBeInTheDocument());

    await user.click(within(card).getByRole('button', { name: '+ إضافة احالة' }));
    await user.selectOptions(screen.getByLabelText('الإدارة *'), '2');
    await user.type(screen.getByLabelText('تاريخ الإحالة *'), '16/01/1448');
    await user.click(screen.getByRole('button', { name: 'حفظ الاحالة' }));

    await waitFor(() => expect(within(card).queryByTestId('assignment-form-panel')).not.toBeInTheDocument());
    expect(confirmSpy).not.toHaveBeenCalled();
  });

  it('closes assignment form via panel close button with dirty confirm', async () => {
    const user = userEvent.setup();
    const confirmSpy = vi.spyOn(globalThis, 'confirm').mockReturnValue(true);

    renderDetail();
    await waitForDetailsReady();
    const card = getAssignmentsCard();
    await waitFor(() => expect(within(card).getByRole('button', { name: '+ إضافة احالة' })).toBeInTheDocument());

    await user.click(within(card).getByRole('button', { name: '+ إضافة احالة' }));
    const panel = within(card).getByTestId('assignment-form-panel');
    await user.type(screen.getByLabelText('الإجراء المطلوب'), 'مسودة');
    await user.click(within(panel).getByRole('button', { name: 'إغلاق النموذج' }));

    expect(confirmSpy).toHaveBeenCalledTimes(1);
    expect(within(card).queryByTestId('assignment-form-panel')).not.toBeInTheDocument();
  });

  it('opens follow-up form inside follow-ups card and saves', async () => {
    const user = userEvent.setup();
    mockApi(services.transactionsApi.addFollowUp).mockResolvedValue({ data: { id: 21 } });
    mockApi(services.transactionsApi.getWorkspace)
      .mockResolvedValueOnce({ data: { ...defaultWorkspace, followUps: [] } })
      .mockResolvedValue({ data: defaultWorkspace });

    renderDetail();
    await waitForDetailsReady();
    const card = getFollowUpsCard();
    await waitFor(() => expect(within(card).getByRole('button', { name: '+ إضافة تعقيب' })).toBeInTheDocument());

    await user.click(within(card).getByRole('button', { name: '+ إضافة تعقيب' }));
    const panel = within(card).getByTestId('followup-form-panel');
    expect(panel).toBeInTheDocument();

    await waitFor(() => expect(screen.getByLabelText('رقم التعقيب')).toBeInTheDocument());
    await user.type(screen.getByLabelText('رقم التعقيب'), 'F-NEW');
    await user.click(screen.getByRole('button', { name: 'حفظ التعقيب' }));

    await waitFor(() => {
      expect(screen.getByRole('status')).toHaveTextContent('تم إضافة التعقيب بنجاح');
      expect(within(card).queryByTestId('followup-form-panel')).not.toBeInTheDocument();
    });
    expect(services.transactionsApi.addFollowUp).toHaveBeenCalledTimes(1);
    expect(mockApi(services.transactionsApi.getWorkspace).mock.calls.length).toBeGreaterThan(1);
  });

  it('opens attachment form inside attachments card and uploads file', async () => {
    const user = userEvent.setup();
    mockApi(services.transactionsApi.uploadAttachment).mockResolvedValue({ data: sampleAttachment });
    mockApi(services.transactionsApi.getWorkspace)
      .mockResolvedValueOnce({ data: { ...defaultWorkspace, attachments: [] } })
      .mockResolvedValue({ data: defaultWorkspace });

    renderDetail();
    await waitForDetailsReady();
    const card = getAttachmentsCard();
    await waitFor(() => expect(within(card).getByRole('button', { name: '+ إضافة مرفق' })).toBeInTheDocument());

    await user.click(within(card).getByRole('button', { name: '+ إضافة مرفق' }));
    const panel = within(card).getByTestId('attachment-form-panel');
    expect(panel).toBeInTheDocument();
    expect(within(panel).getByText('اختيار ملف من الجهاز')).toBeInTheDocument();
    expect(within(panel).getByRole('button', { name: 'إلغاء' })).toBeInTheDocument();

    const fileInput = panel.querySelector('input[type="file"]') as HTMLInputElement;
    const file = new File(['pdf'], 'scan.pdf', { type: 'application/pdf' });
    await user.upload(fileInput, file);

    await waitFor(() => {
      expect(screen.getByRole('status')).toHaveTextContent('تم رفع المرفق بنجاح');
      expect(within(card).queryByTestId('attachment-form-panel')).not.toBeInTheDocument();
      expect(screen.getByText('file.pdf')).toBeInTheDocument();
    });
    expect(services.transactionsApi.uploadAttachment).toHaveBeenCalledTimes(1);
    expect(mockApi(services.transactionsApi.getWorkspace).mock.calls.length).toBeGreaterThan(1);
  });

  it('shows only one inline add form at a time when switching cards', async () => {
    const user = userEvent.setup();
    renderDetail();
    await waitForDetailsReady();

    await user.click(within(getAssignmentsCard()).getByRole('button', { name: '+ إضافة احالة' }));
    expect(screen.getByTestId('assignment-form-panel')).toBeInTheDocument();

    await user.click(within(getFollowUpsCard()).getByRole('button', { name: '+ إضافة تعقيب' }));
    expect(screen.queryByTestId('assignment-form-panel')).not.toBeInTheDocument();
    expect(within(getFollowUpsCard()).getByTestId('followup-form-panel')).toBeInTheDocument();
  });

  it('opens reply form inside assignments card for pending assignment', async () => {
    const user = userEvent.setup();
    mockApi(services.transactionsApi.getAssignments).mockResolvedValue({ data: [sampleAssignment] });
    mockApi(services.transactionsApi.replyAssignment).mockResolvedValue({ data: {} });

    renderDetail();
    await waitForDetailsReady();
    const card = getAssignmentsCard();
    await waitFor(() => expect(within(card).getByRole('button', { name: 'تسجيل رد' })).toBeInTheDocument());

    await user.click(within(card).getByRole('button', { name: 'تسجيل رد' }));
    expect(within(card).getByTestId('reply-assignment-form-panel')).toBeInTheDocument();

    expect(within(card).getByRole('region', { name: 'تسجيل إفادة الإدارة' })).toBeInTheDocument();
    expect(screen.getByLabelText('تاريخ إنجاز الإدارة *')).toHaveValue('');
    expect(screen.getByText('يمثل تاريخ الإفادة/إنجاز رد الإدارة، ويستخدم في احتساب أيام إنجاز الإدارة.')).toBeInTheDocument();
    await user.type(screen.getByLabelText(/ملخص الإفادة/), 'تم الرد');
    await user.click(screen.getByRole('button', { name: 'حفظ الإفادة' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('تاريخ إنجاز الإدارة مطلوب.');
    expect(services.transactionsApi.replyAssignment).not.toHaveBeenCalled();

    await user.type(screen.getByLabelText('تاريخ إنجاز الإدارة *'), '16/01/1448');
    await user.click(screen.getByRole('button', { name: 'حفظ الإفادة' }));

    await waitFor(() => {
      expect(screen.getByRole('status')).toHaveTextContent('تم تسجيل الرد بنجاح');
      expect(within(card).queryByTestId('reply-assignment-form-panel')).not.toBeInTheDocument();
    });
    expect(services.transactionsApi.replyAssignment).toHaveBeenCalledWith(1, 10, {
      replyDate: '2026-07-01T00:00:00',
      replySummary: 'تم الرد',
    });
  });

  it('shows transaction outgoing number as assignment letter fallback', async () => {
    mockApi(services.transactionsApi.getWorkspace).mockResolvedValue({
      data: {
        ...defaultWorkspace,
        transaction: {
          ...baseTx,
          outgoingNumber: 'OUT-77',
        },
        assignments: [{
          ...sampleAssignment,
          letterNumber: null,
        }],
      },
    });

    renderDetail();
    await waitForDetailsReady();

    expect(within(getAssignmentsCard()).getByText('OUT-77')).toBeInTheDocument();
  });

  it('lets Admin open assignment edit from the department name without a separate edit button', async () => {
    const user = userEvent.setup();
    const editableAssignment = {
        ...sampleAssignment,
        assignedDate: '2026-01-02',
        canAdminEdit: true,
      };
    mockApi(services.transactionsApi.getWorkspace).mockResolvedValue({
      data: {
        ...defaultWorkspace,
        assignments: [editableAssignment],
      },
    });
    mockApi(services.transactionsApi.getAssignments).mockResolvedValue({
      data: [editableAssignment],
    });

    renderDetail();
    await waitForDetailsReady();
    const card = getAssignmentsCard();

    const departmentButton = await within(card).findByRole('button', { name: 'تعديل إحالة إدارة إدارة اختبار' });
    expect(departmentButton).toHaveTextContent('إدارة اختبار');
    expect(within(card).queryByText(/^تعديل$/)).not.toBeInTheDocument();
    expect(within(card).queryByRole('button', { name: 'تعديل' })).not.toBeInTheDocument();

    await user.click(departmentButton);

    expect(within(card).getByTestId('admin-edit-assignment-form-panel')).toBeInTheDocument();
    expect(screen.getByLabelText('الإجراء المطلوب')).toHaveValue('مراجعة');
  });

  it('renders department name as plain text for DepartmentUser', async () => {
    mockUseAuth.mockReturnValue({
      canEdit: false,
      canClose: false,
      isDepartmentUser: true,
      user: {
        fullName: 'موظف إدارة',
        role: 'DepartmentUser',
        departmentId: 1,
      },
      logout: vi.fn(),
      login: vi.fn(),
      isAdmin: false,
    });

    renderDetail();
    await waitForDetailsReady();
    const card = getAssignmentsCard();

    expect(within(card).getByText('إدارة اختبار')).toBeInTheDocument();
    expect(within(card).getByText('بانتظار الرد')).toBeInTheDocument();
    expect(within(card).queryByRole('button', { name: 'تعديل إحالة إدارة إدارة اختبار' })).not.toBeInTheDocument();
  });

  it('lets Admin open response edit from completed response status', async () => {
    const user = userEvent.setup();
    const repliedAssignment = {
      ...sampleAssignment,
      replyStatus: 'Replied',
      responseDate: '2026-01-05',
      departmentResponseId: 100,
    };
    mockApi(services.transactionsApi.getWorkspace).mockResolvedValue({
      data: {
        ...defaultWorkspace,
        assignments: [repliedAssignment],
      },
    });
    mockApi(services.transactionsApi.getAssignments).mockResolvedValue({
      data: [repliedAssignment],
    });

    renderDetail();
    await waitForDetailsReady();
    const card = getAssignmentsCard();

    const responseButton = await within(card).findByRole('button', { name: 'تعديل إفادة إدارة إدارة اختبار' });
    expect(responseButton).toHaveTextContent('تمت الإفادة');
    expect(within(card).queryByText('تعديل الرد')).not.toBeInTheDocument();
    expect(within(card).queryByRole('button', { name: 'تعديل الرد' })).not.toBeInTheDocument();

    await user.click(responseButton);

    expect(within(card).getByTestId('admin-edit-response-form-panel')).toBeInTheDocument();
    expect(await screen.findByLabelText('ملخص الإفادة')).toHaveValue('نص الإفادة');
  });

  it('does not make completed response status editable without departmentResponseId', async () => {
    const repliedAssignment = {
      ...sampleAssignment,
      replyStatus: 'Replied',
      responseDate: '2026-01-05',
      departmentResponseId: undefined,
    };
    mockApi(services.transactionsApi.getWorkspace).mockResolvedValue({
      data: {
        ...defaultWorkspace,
        assignments: [repliedAssignment],
      },
    });
    mockApi(services.transactionsApi.getAssignments).mockResolvedValue({
      data: [repliedAssignment],
    });

    renderDetail();
    await waitForDetailsReady();
    const card = getAssignmentsCard();

    expect(within(card).getByText('تمت الإفادة')).toBeInTheDocument();
    expect(within(card).queryByRole('button', { name: 'تعديل إفادة إدارة إدارة اختبار' })).not.toBeInTheDocument();
  });

  it('shows an error when the workspace refresh after an admin response edit fails', async () => {
    const user = userEvent.setup();
    const repliedAssignment = {
      ...sampleAssignment,
      replyStatus: 'Replied',
      responseDate: '2026-01-05',
      departmentResponseId: 100,
    };
    mockApi(services.transactionsApi.getWorkspace)
      .mockResolvedValueOnce({
        data: { ...defaultWorkspace, assignments: [repliedAssignment] },
      })
      .mockRejectedValueOnce(new Error('network error'));
    mockApi(services.transactionsApi.getAssignments).mockResolvedValue({
      data: [repliedAssignment],
    });

    renderDetail();
    await waitForDetailsReady();
    const card = getAssignmentsCard();

    const responseButton = await within(card).findByRole('button', { name: 'تعديل إفادة إدارة إدارة اختبار' });
    await user.click(responseButton);

    expect(within(card).getByTestId('admin-edit-response-form-panel')).toBeInTheDocument();
    const summaryField = await screen.findByLabelText('ملخص الإفادة');
    await user.type(summaryField, ' إضافة');
    await user.type(screen.getByLabelText(/سبب التعديل/), 'تصحيح الإفادة');

    await user.click(screen.getByRole('button', { name: 'حفظ التصحيح' }));

    // loadWorkspace surfaces its own failure via the page-level error state; the
    // fix under test ensures handleAdminEditResponseSuccess awaits that refresh
    // instead of a fire-and-forget `.catch(() => undefined)` that discarded it.
    await waitFor(() => {
      expect(screen.getByText('تعذر تحميل بيانات المعاملة')).toBeInTheDocument();
    });
  });

  it('renders completed response status as plain text for DepartmentUser', async () => {
    const repliedAssignment = {
      ...sampleAssignment,
      replyStatus: 'Replied',
      responseDate: '2026-01-05',
      departmentResponseId: 100,
      canAdminEdit: true,
    };
    mockUseAuth.mockReturnValue({
      canEdit: false,
      canClose: false,
      isDepartmentUser: true,
      user: {
        fullName: 'موظف إدارة',
        role: 'DepartmentUser',
        departmentId: 1,
      },
      logout: vi.fn(),
      login: vi.fn(),
      isAdmin: false,
    });
    mockApi(services.transactionsApi.getWorkspace).mockResolvedValue({
      data: {
        ...defaultWorkspace,
        assignments: [repliedAssignment],
      },
    });
    mockApi(services.transactionsApi.getAssignments).mockResolvedValue({
      data: [repliedAssignment],
    });

    renderDetail();
    await waitForDetailsReady();
    const card = getAssignmentsCard();

    expect(within(card).getByText('إدارة اختبار')).toBeInTheDocument();
    expect(within(card).getByText('تمت الإفادة')).toBeInTheDocument();
    expect(within(card).queryByRole('button', { name: 'تعديل إحالة إدارة إدارة اختبار' })).not.toBeInTheDocument();
    expect(within(card).queryByRole('button', { name: 'تعديل إفادة إدارة إدارة اختبار' })).not.toBeInTheDocument();
  });
});

describe('TransactionDetailPage current status and response workflow', () => {
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
    mockApi(services.transactionsApi.getAssignments).mockResolvedValue({ data: [] });
    mockApi(services.transactionsApi.getFollowUps).mockResolvedValue({ data: [] });
    mockApi(services.transactionsApi.getAttachments).mockResolvedValue({ data: [] });
  });

  afterEach(() => {
    vi.restoreAllMocks();
    cleanup();
  });

  it('does not render the removed workspace title or transaction-context bar', async () => {
    renderDetail();
    await waitForDetailsReady();

    expect(screen.queryByText('مساحة عمل المعاملة')).not.toBeInTheDocument();
    expect(screen.queryByTestId('transaction-context')).not.toBeInTheDocument();
    expect(document.querySelector('.transaction-context-bar')).not.toBeInTheDocument();
  });

  it('shows the pending departments in the current action/status area', async () => {
    mockApi(services.transactionsApi.getWorkspace).mockResolvedValue({
      data: {
        ...defaultWorkspace,
        transaction: {
          ...baseTx,
          requiresResponse: true,
          responseType: 'Internal',
          pendingDepartmentNames: ['إدارة أولى', 'إدارة ثانية'],
        },
      },
    });

    renderDetail();
    await waitForDetailsReady();

    const statusCard = getCurrentStatusCard();
    expect(within(statusCard).getByText('بانتظار رد إدارات')).toBeInTheDocument();
    expect(within(statusCard).getByText(/إدارة أولى، إدارة ثانية/)).toBeInTheDocument();
  });

  it('shows an empty state in the الإفادة card when no response has been registered', async () => {
    mockApi(services.transactionsApi.getWorkspace).mockResolvedValue({
      data: {
        ...defaultWorkspace,
        transaction: {
          ...baseTx,
          requiresResponse: true,
          responseType: 'Internal',
        },
      },
    });

    renderDetail();
    await waitForDetailsReady();

    const responseCard = getResponseCard();
    expect(within(responseCard).getByText('لم يتم تسجيل إفادة لهذه المعاملة بعد.')).toBeInTheDocument();
  });

  it('shows the completed response details in the الإفادة card', async () => {
    mockApi(services.transactionsApi.getWorkspace).mockResolvedValue({
      data: {
        ...defaultWorkspace,
        transaction: {
          ...baseTx,
          requiresResponse: true,
          responseType: 'External',
          responseCompleted: true,
          responseCompletedDate: '2026-01-10',
          responseSummary: 'تم الرد على المعاملة',
          outgoingNumber: 'OUT-1',
          outgoingDate: '2026-01-10',
        },
      },
    });

    renderDetail();
    await waitForDetailsReady();

    const responseCard = getResponseCard();
    expect(within(responseCard).getByText('تمت الإفادة')).toBeInTheDocument();
    expect(within(responseCard).getByText('تم الرد على المعاملة')).toBeInTheDocument();
    expect(within(responseCard).getByText('OUT-1')).toBeInTheDocument();
  });

  it('lets an authorized admin open the completed response for editing by clicking تمت الإفادة, prefilled with the existing values, and updates the same record without creating a duplicate', async () => {
    const user = userEvent.setup();
    const completedTx = {
      ...baseTx,
      requiresResponse: true,
      responseType: 'External',
      responseCompleted: true,
      responseCompletedDate: '2026-01-10',
      responseSummary: 'ملخص أصلي',
      outgoingNumber: 'OUT-1',
      outgoingDate: '2026-01-10',
    };
    const updatedTx = { ...completedTx, responseSummary: 'ملخص محدث' };
    mockApi(services.transactionsApi.getWorkspace)
      .mockResolvedValueOnce({ data: { ...defaultWorkspace, transaction: completedTx } })
      .mockResolvedValue({ data: { ...defaultWorkspace, transaction: updatedTx } });
    mockApi(services.transactionsApi.getBasic).mockResolvedValue({ data: completedTx });
    mockApi(services.transactionsApi.editResponse).mockResolvedValue({
      data: updatedTx,
    });

    renderDetail();
    await waitForDetailsReady();

    const responseCard = getResponseCard();
    const editButton = within(responseCard).getByRole('button', { name: 'تعديل الإفادة المسجلة' });
    expect(editButton).toHaveTextContent('تمت الإفادة');

    await user.click(editButton);

    const panel = within(responseCard).getByTestId('admin-edit-transaction-response-form-panel');
    const summaryField = await within(panel).findByLabelText('ملخص الإفادة *');
    expect(summaryField).toHaveValue('ملخص أصلي');
    expect(within(panel).getByLabelText('رقم الصادر *')).toHaveValue('OUT-1');

    await user.clear(summaryField);
    await user.type(summaryField, 'ملخص محدث');
    await user.click(within(panel).getByRole('button', { name: 'حفظ التعديلات' }));

    await waitFor(() => {
      expect(services.transactionsApi.editResponse).toHaveBeenCalledTimes(1);
    });
    expect(services.transactionsApi.completeResponse).not.toHaveBeenCalled();
    await waitFor(() => {
      expect(within(getResponseCard()).getByText('ملخص محدث')).toBeInTheDocument();
    });
    expect(screen.getAllByRole('region', { name: 'الإفادة' })).toHaveLength(1);
  });

  it('renders the completed response status as plain text for users without edit permission', async () => {
    mockUseAuth.mockReturnValue({
      canEdit: false,
      canClose: false,
      isDepartmentUser: false,
      user: { fullName: 'قارئ', role: 'Reader' },
      logout: vi.fn(),
      login: vi.fn(),
      isAdmin: false,
    });
    mockApi(services.transactionsApi.getWorkspace).mockResolvedValue({
      data: {
        ...defaultWorkspace,
        transaction: {
          ...baseTx,
          requiresResponse: true,
          responseType: 'Internal',
          responseCompleted: true,
          responseCompletedDate: '2026-01-10',
          responseSummary: 'ملخص',
        },
      },
    });

    renderDetail();
    await waitForDetailsReady();

    const responseCard = getResponseCard();
    expect(within(responseCard).getByText('تمت الإفادة')).toBeInTheDocument();
    expect(within(responseCard).queryByRole('button', { name: 'تعديل الإفادة المسجلة' })).not.toBeInTheDocument();
  });

  it.each(['Closed', 'Cancelled', 'Archived'])(
    'hides the تمت الإفادة edit affordance for %s transactions',
    async (status) => {
      mockApi(services.transactionsApi.getWorkspace).mockResolvedValue({
        data: {
          ...defaultWorkspace,
          transaction: {
            ...baseTx,
            status,
            requiresResponse: true,
            responseType: 'Internal',
            responseCompleted: true,
            responseCompletedDate: '2026-01-10',
            responseSummary: 'ملخص',
          },
        },
      });

      renderDetail();
      await waitForDetailsReady();

      const responseCard = getResponseCard();
      expect(within(responseCard).getByText('تمت الإفادة')).toBeInTheDocument();
      expect(within(responseCard).queryByRole('button', { name: 'تعديل الإفادة المسجلة' })).not.toBeInTheDocument();
    },
  );

  it('renders pending department names safely when the field is null or undefined', async () => {
    mockApi(services.transactionsApi.getWorkspace).mockResolvedValue({
      data: {
        ...defaultWorkspace,
        transaction: {
          ...baseTx,
          requiresResponse: true,
          responseType: 'Internal',
          pendingDepartmentNames: null,
        },
      },
    });

    renderDetail();
    await waitForDetailsReady();

    const statusCard = getCurrentStatusCard();
    expect(within(statusCard).queryByText(/الإدارات المتبقية/)).not.toBeInTheDocument();
    expect(within(statusCard).getByText('جاهزة لتسجيل الإفادة')).toBeInTheDocument();
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
    mockApi(services.transactionsApi.getAssignments).mockResolvedValue({ data: [] });
  });

  afterEach(() => {
    vi.restoreAllMocks();
    cleanup();
  });

  it('loads transaction data automatically and shows hero summary', async () => {
    renderDetail();

    await waitFor(() => {
      expect(screen.getByRole('heading', { level: 2, name: 'IN-1' })).toBeInTheDocument();
    });
    expect(screen.getByRole('navigation', { name: 'إجراءات المعاملة' })).toBeInTheDocument();
    expect(within(getAssignmentsCard()).getByRole('button', { name: '+ إضافة احالة' })).toBeInTheDocument();
    expect(screen.getByText('محسوب تلقائيًا: اليوم − تاريخ الوارد')).toBeInTheDocument();
    const openDaysMetric = screen.getByText('الأيام المفتوحة').closest('.transaction-metric-tile');
    expect(openDaysMetric).not.toBeNull();
    expect(within(openDaysMetric as HTMLElement).getByText('1 يوم')).toBeInTheDocument();
  });

  it('shows transaction completion days when available', async () => {
    const completedTransaction = {
      ...baseTx,
      completionDate: '2026-01-04',
      completionDays: 3,
    };
    mockApi(services.transactionsApi.getWorkspace).mockResolvedValue({
      data: {
        ...defaultWorkspace,
        transaction: completedTransaction,
        temporalFacts: {
          ...defaultTemporalFacts,
          completionDays: 3,
        },
      },
    });
    mockApi(services.transactionsApi.getBasic).mockResolvedValue({ data: completedTransaction });
    renderDetail();

    await waitFor(() => {
      expect(screen.getByText('أيام إنجاز المعاملة')).toBeInTheDocument();
    });
    const completionMetric = screen.getByText('أيام إنجاز المعاملة').closest('.transaction-metric-tile');
    expect(completionMetric).not.toBeNull();
    expect(within(completionMetric as HTMLElement).getByText('3 أيام')).toBeInTheDocument();
    expect(within(completionMetric as HTMLElement).getByText('محسوب تلقائيًا: تاريخ الإغلاق − تاريخ الوارد')).toBeInTheDocument();
  });

  it('opens inline assignment form from action bar in hero area', async () => {
    const user = userEvent.setup();
    renderDetail();

    await waitFor(() => {
      expect(within(getAssignmentsCard()).getByRole('button', { name: '+ إضافة احالة' })).toBeInTheDocument();
    });

    await user.click(within(getAssignmentsCard()).getByRole('button', { name: '+ إضافة احالة' }));

    expect(within(getAssignmentsCard()).getByTestId('assignment-form-panel')).toBeInTheDocument();
    expect(screen.getByLabelText('الإدارة *')).toBeInTheDocument();
  });

  it('does not confirm after successful assignment save from action bar', async () => {
    const user = userEvent.setup();
    const confirmSpy = vi.spyOn(globalThis, 'confirm');
    mockApi(services.transactionsApi.addAssignment).mockResolvedValue({ data: { id: 9 } });

    renderDetail();
    await waitFor(() => expect(within(getAssignmentsCard()).getByRole('button', { name: '+ إضافة احالة' })).toBeInTheDocument());

    await user.click(within(getAssignmentsCard()).getByRole('button', { name: '+ إضافة احالة' }));
    await user.selectOptions(screen.getByLabelText('الإدارة *'), '2');
    await user.type(screen.getByLabelText('تاريخ الإحالة *'), '16/01/1448');
    await user.type(screen.getByLabelText('الإجراء المطلوب'), 'مراجعة');
    await user.click(screen.getByRole('button', { name: 'حفظ الاحالة' }));

    await waitFor(() => {
      expect(screen.getByRole('status')).toHaveTextContent('تم إضافة الاحالة بنجاح');
      expect(screen.queryByTestId('assignment-form-panel')).not.toBeInTheDocument();
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
    vi.restoreAllMocks();
    cleanup();
  });

  it('hides mutation actions for reader role across cards and action bar', async () => {
    expect(vi.isMockFunction(globalThis.confirm)).toBe(false);

    renderDetail();

    await waitFor(() => expect(screen.getByRole('heading', { level: 2, name: 'IN-1' })).toBeInTheDocument());
    expect(screen.queryByRole('button', { name: 'إضافة احالة' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '+ إضافة احالة' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'إضافة تعقيب' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '+ إضافة مرفق' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'إضافة أول احالة' })).not.toBeInTheDocument();
  });

  it('hides admin mutation and reply actions for department user', async () => {
    mockUseAuth.mockReturnValue({
      canEdit: true,
      canClose: false,
      isDepartmentUser: true,
      user: { fullName: 'موظف', role: 'DepartmentUser', departmentId: 1 },
      logout: vi.fn(),
      login: vi.fn(),
      isAdmin: false,
    });

    renderDetail();

    await waitFor(() => expect(screen.getByRole('heading', { level: 2, name: 'IN-1' })).toBeInTheDocument());
    expect(screen.queryByRole('button', { name: '+ إضافة احالة' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '+ إضافة تعقيب' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'تعديل' })).not.toBeInTheDocument();
    expect(within(getAssignmentsCard()).queryByRole('button', { name: 'تسجيل رد' })).not.toBeInTheDocument();
  });

  it('shows mutation buttons for supervisor with edit permission', async () => {
    mockUseAuth.mockReturnValue({
      canEdit: true,
      canClose: false,
      isDepartmentUser: false,
      user: { fullName: 'مشرف', role: 'Supervisor' },
      logout: vi.fn(),
      login: vi.fn(),
      isAdmin: false,
    });
    mockApi(services.transactionsApi.getAssignments).mockResolvedValue({ data: [] });

    renderDetail();

    await waitFor(() => {
      expect(within(getAssignmentsCard()).getByRole('button', { name: '+ إضافة احالة' })).toBeInTheDocument();
      expect(within(getFollowUpsCard()).getByRole('button', { name: '+ إضافة تعقيب' })).toBeInTheDocument();
      expect(within(getAttachmentsCard()).getByRole('button', { name: '+ إضافة مرفق' })).toBeInTheDocument();
    });
  });
});

describe('TransactionDetailPage Admin/Supervisor response form', () => {
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
    mockApi(services.transactionsApi.getWorkspace).mockResolvedValue({
      data: {
        ...defaultWorkspace,
        transaction: {
          ...baseTx,
          requiresResponse: true,
          responseType: 'Internal',
          responseCompleted: false,
          pendingDepartmentNames: [],
        },
      },
    });
    mockApi(services.transactionsApi.completeResponse).mockResolvedValue({ data: {} });
    mockApi(services.transactionsApi.uploadAttachment).mockResolvedValue({ data: {} });
  });

  afterEach(() => {
    vi.restoreAllMocks();
    cleanup();
  });

  it('shows CompleteResponseFormPanel as compact editor when Admin clicks تسجيل إفادة', async () => {
    const user = userEvent.setup();
    renderDetail();
    await waitForDetailsReady();

    await waitFor(() => expect(within(getActionBar()).getByRole('button', { name: 'تسجيل إفادة' })).toBeInTheDocument());
    await user.click(within(getActionBar()).getByRole('button', { name: 'تسجيل إفادة' }));

    const panel = screen.getByRole('region', { name: 'تسجيل إفادة' });
    expect(within(panel).getByLabelText('ملخص الإفادة *')).toHaveAttribute('rows', '4');
    expect(within(panel).getByRole('button', { name: 'إرسال الإفادة' })).toBeInTheDocument();
    expect(within(panel).getByRole('button', { name: 'إلغاء' })).toBeInTheDocument();
    expect(within(panel).getByRole('button', { name: 'رفع ملف' })).toBeInTheDocument();
    expect(within(panel).getByRole('button', { name: 'مسح ضوئي' })).toBeInTheDocument();
  });

  it('uses fieldset for attachment toolbar — no role=group', async () => {
    const user = userEvent.setup();
    const { container } = renderDetail();
    await waitForDetailsReady();

    await waitFor(() => expect(within(getActionBar()).getByRole('button', { name: 'تسجيل إفادة' })).toBeInTheDocument());
    await user.click(within(getActionBar()).getByRole('button', { name: 'تسجيل إفادة' }));

    await waitFor(() => expect(screen.getByRole('region', { name: 'تسجيل إفادة' })).toBeInTheDocument());
    expect(container.querySelector('fieldset.complete-response-attachment-toolbar')).toBeInTheDocument();
    expect(container.querySelector('[role="group"]')).not.toBeInTheDocument();
  });

  it('submits response successfully for Admin', async () => {
    const user = userEvent.setup();
    renderDetail();
    await waitForDetailsReady();

    await waitFor(() => expect(within(getActionBar()).getByRole('button', { name: 'تسجيل إفادة' })).toBeInTheDocument());
    await user.click(within(getActionBar()).getByRole('button', { name: 'تسجيل إفادة' }));

    const panel = screen.getByRole('region', { name: 'تسجيل إفادة' });
    await user.type(within(panel).getByLabelText('ملخص الإفادة *'), 'ملخص الإفادة');
    await user.click(within(panel).getByRole('button', { name: 'إرسال الإفادة' }));

    await waitFor(() => {
      expect(services.transactionsApi.completeResponse).toHaveBeenCalledTimes(1);
      expect(screen.getByRole('status')).toHaveTextContent('تم تسجيل الإفادة بنجاح');
    });
    expect(screen.queryByRole('region', { name: 'تسجيل إفادة' })).not.toBeInTheDocument();
  });

  it('does not show DepartmentResponseInlinePanel for non-department users', async () => {
    const user = userEvent.setup();
    renderDetail();
    await waitForDetailsReady();

    await waitFor(() => expect(within(getActionBar()).getByRole('button', { name: 'تسجيل إفادة' })).toBeInTheDocument());
    await user.click(within(getActionBar()).getByRole('button', { name: 'تسجيل إفادة' }));

    await waitFor(() => expect(screen.getByRole('region', { name: 'تسجيل إفادة' })).toBeInTheDocument());
    expect(screen.queryByRole('heading', { name: 'إفادة الإدارة' })).not.toBeInTheDocument();
  });
});

describe('TransactionDetailPage recurring template info', () => {
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

  it('shows recurring template card and links when the transaction was generated from a template', async () => {
    const recurringTx = {
      ...baseTx,
      recurringTemplateId: 7,
      recurringTemplateTitle: 'تقرير شهري من إدارة التشغيل',
      recurringPeriodKey: '2026-01',
      recurringPeriodLabel: 'يناير 2026',
    };
    mockApi(services.transactionsApi.getWorkspace).mockResolvedValue({
      data: { ...defaultWorkspace, transaction: recurringTx },
    });
    mockApi(services.transactionsApi.getBasic).mockResolvedValue({ data: recurringTx });

    renderDetail();
    await waitForDetailsReady();

    const infoBar = await screen.findByTestId('recurring-template-info');
    expect(infoBar).toHaveTextContent('معاملة دورية');
    expect(infoBar).toHaveTextContent('تقرير شهري من إدارة التشغيل');
    expect(infoBar).toHaveTextContent('يناير 2026');
    expect(within(infoBar).getByRole('link', { name: 'الرجوع إلى القالب' }))
      .toHaveAttribute('href', '/recurring-transaction-templates?highlight=7');
    expect(within(infoBar).getByRole('link', { name: 'عرض معاملات نفس القالب' }))
      .toHaveAttribute('href', '/recurring-transaction-templates?viewTransactions=7');
  });

  it('does not show recurring template card for regular transactions', async () => {
    renderDetail();
    await waitForDetailsReady();

    expect(screen.queryByTestId('recurring-template-info')).not.toBeInTheDocument();
  });

  it('shows the enable-recurring action for a transaction with no recurring link', async () => {
    renderDetail();
    await waitForDetailsReady();

    expect(screen.getByRole('button', { name: 'تفعيل متابعة دورية لهذه المعاملة' })).toBeInTheDocument();
  });

  it('does not show the enable-recurring action once a transaction is already linked to a template', async () => {
    const recurringTx = { ...baseTx, recurringTemplateId: 7, recurringTemplateTitle: 'قالب', recurringPeriodLabel: 'يناير 2026' };
    mockApi(services.transactionsApi.getWorkspace).mockResolvedValue({
      data: { ...defaultWorkspace, transaction: recurringTx },
    });
    mockApi(services.transactionsApi.getBasic).mockResolvedValue({ data: recurringTx });

    renderDetail();
    await waitForDetailsReady();

    expect(screen.queryByRole('button', { name: 'تفعيل متابعة دورية لهذه المعاملة' })).not.toBeInTheDocument();
  });

  it('enables recurring follow-up from the transaction detail page', async () => {
    const user = userEvent.setup();
    mockApi(services.transactionsApi.enableRecurring).mockResolvedValue({
      data: {
        ...baseTx,
        recurringTemplateId: 9,
        recurringTemplateTitle: 'موضوع',
        recurringPeriodKey: '2026-01',
        recurringPeriodLabel: 'يناير 2026',
      },
    });

    renderDetail();
    await waitForDetailsReady();

    await user.click(screen.getByRole('button', { name: 'تفعيل متابعة دورية لهذه المعاملة' }));
    await waitFor(() => expect(screen.getByRole('region', { name: 'تفعيل متابعة دورية' })).toBeInTheDocument());

    const panel = screen.getByRole('region', { name: 'تفعيل متابعة دورية' });
    await user.click(within(panel).getByRole('button', { name: 'تفعيل المتابعة الدورية' }));

    await waitFor(() => expect(services.transactionsApi.enableRecurring).toHaveBeenCalledTimes(1));
    expect(mockApi(services.transactionsApi.enableRecurring).mock.calls[0][0]).toBe(1);
    await waitFor(() => expect(screen.getByTestId('recurring-template-info')).toBeInTheDocument());
  });

  it('suggests generating the next period after closing a transaction with AutomaticOnClose', async () => {
    const user = userEvent.setup();
    const recurringTx = {
      ...baseTx,
      recurringTemplateId: 7,
      recurringTemplateTitle: 'قالب',
      recurringPeriodLabel: 'يناير 2026',
      requiresResponse: false,
      responseType: 'None',
    };
    mockApi(services.transactionsApi.getWorkspace).mockResolvedValue({
      data: { ...defaultWorkspace, transaction: recurringTx },
    });
    mockApi(services.transactionsApi.getBasic).mockResolvedValue({ data: recurringTx });
    mockApi(services.transactionsApi.close).mockResolvedValue({});
    mockApi(services.recurringTemplatesApi.getById).mockResolvedValue({
      data: {
        id: 7,
        status: 'Active',
        nextTransactionCreationMethod: 'AutomaticOnClose',
        nextPeriodLabel: 'فبراير 2026',
      },
    });
    vi.spyOn(globalThis, 'confirm').mockReturnValue(true);

    renderDetail();
    await waitForDetailsReady();

    await user.click(getActionBarButton('إغلاق المعاملة'));

    await waitFor(() => expect(services.transactionsApi.close).toHaveBeenCalled());
    await waitFor(() => {
      expect(screen.getByRole('link', { name: 'إنشاء معاملة الفترة القادمة' })).toBeInTheDocument();
    });
    expect(screen.getByRole('link', { name: 'إنشاء معاملة الفترة القادمة' }))
      .toHaveAttribute('href', '/recurring-transaction-templates?generate=7');
  });
});
