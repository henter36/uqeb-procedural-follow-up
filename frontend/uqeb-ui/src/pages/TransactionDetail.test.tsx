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
    getAdjacent: vi.fn(),
    getWorkspace: vi.fn(),
    getAssignments: vi.fn(),
    getFollowUps: vi.fn(),
    getAttachments: vi.fn(),
    getAuditLog: vi.fn(),
    addAssignment: vi.fn(),
    addFollowUp: vi.fn(),
    uploadAttachment: vi.fn(),
    replyAssignment: vi.fn(),
    editAssignmentReply: vi.fn(),
    replyFollowUp: vi.fn(),
    editFollowUpReply: vi.fn(),
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
  return screen.getByRole('region', { name: 'الإحالات والردود' });
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
  return screen.getByTestId('current-action-status-tile');
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
  mockApi(services.transactionsApi.getAdjacent).mockResolvedValue({ data: { previousId: null, nextId: null } });
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
    expect(screen.getByRole('columnheader', { name: 'تاريخ إنجاز الإدارة' })).toBeInTheDocument();
    const assignmentsCard = getAssignmentsCard();
    expect(within(assignmentsCard).getByText('عمر المعاملة: 1 يوم')).toBeInTheDocument();
    expect(within(assignmentsCard).getAllByText(/عمر المعاملة:/)).toHaveLength(1);
    expect(within(assignmentsCard).getByText('عمر المعاملة: 1 يوم').closest('.transaction-metric-tile')).toBeNull();
    expect(within(assignmentsCard).queryByText('مراجعة')).not.toBeInTheDocument();
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
    expect(within(card).getByRole('button', { name: 'تمت الإفادة - تعديل إفادة الإحالة' })).toBeInTheDocument();
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
    await user.type(screen.getByLabelText('تاريخ إنجاز الإدارة *'), '16/01/1448');
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
    await user.type(screen.getByLabelText('تاريخ التعقيب *'), '16/01/1448');
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
      replySummary: 'نص الإفادة',
      departmentResponseId: null,
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

    const responseButton = await within(card).findByRole('button', { name: 'تمت الإفادة - تعديل إفادة الإحالة' });
    expect(responseButton).toHaveTextContent('تمت الإفادة');
    expect(within(card).queryByText('تعديل الرد')).not.toBeInTheDocument();
    expect(within(card).queryByRole('button', { name: 'تعديل الرد' })).not.toBeInTheDocument();

    await user.click(responseButton);

    expect(within(card).getByTestId('admin-edit-assignment-reply-form-panel')).toBeInTheDocument();
    expect(await screen.findByLabelText('ملخص الإفادة *')).toHaveValue('نص الإفادة');
  });

  it('renders the row-level edit button for a real-world replied المالية row with populated response fields', async () => {
    const financeRow = {
      ...sampleAssignment,
      departmentName: 'المالية',
      replyStatus: 'Replied',
      replyDate: '2026-07-07T00:00:00',
      replySummary: 'طذطذ',
      responseDate: '2026-07-07T00:00:00',
      departmentResponseId: null,
      canAdminEdit: true,
    };
    mockApi(services.transactionsApi.getWorkspace).mockResolvedValue({
      data: { ...defaultWorkspace, assignments: [financeRow] },
    });
    mockApi(services.transactionsApi.getAssignments).mockResolvedValue({ data: [financeRow] });

    renderDetail();
    await waitForDetailsReady();
    const card = getAssignmentsCard();
    const row = within(card).getByText('المالية').closest('tr')!;

    expect(within(row).getByRole('button', { name: 'تمت الإفادة - تعديل إفادة الإحالة' })).toBeInTheDocument();
  });

  it('edits the real-world المالية assignment reply by assignment.id when departmentResponseId is null', async () => {
    const user = userEvent.setup();
    const assignment = {
      ...sampleAssignment,
      id: 3,
      departmentName: 'المالية',
      replyStatus: 'Replied',
      replyDate: '2026-07-07T00:00:00',
      responseDate: '2026-07-07T00:00:00',
      replySummary: 'طذطذ',
      departmentResponseId: null,
    };
    mockApi(services.transactionsApi.getWorkspace).mockResolvedValue({
      data: { ...defaultWorkspace, assignments: [assignment] },
    });
    mockApi(services.transactionsApi.getAssignments).mockResolvedValue({ data: [assignment] });
    mockApi(services.transactionsApi.editAssignmentReply).mockResolvedValue({
      data: { ...assignment, replySummary: 'طذطذ محدث' },
    });

    renderDetail();
    await waitForDetailsReady();
    const card = getAssignmentsCard();
    const row = within(card).getByText('المالية').closest('tr')!;

    const button = within(row).getByRole('button', { name: 'تمت الإفادة - تعديل إفادة الإحالة' });
    expect(button).toHaveTextContent('تمت الإفادة');

    await user.click(button);

    const panel = within(card).getByTestId('admin-edit-assignment-reply-form-panel');
    expect(await within(panel).findByLabelText('ملخص الإفادة *')).toHaveValue('طذطذ');

    const summaryField = within(panel).getByLabelText('ملخص الإفادة *');
    await user.clear(summaryField);
    await user.type(summaryField, 'طذطذ محدث');
    await user.click(within(panel).getByRole('button', { name: 'حفظ التعديلات' }));

    await waitFor(() => {
      expect(services.transactionsApi.editAssignmentReply).toHaveBeenCalledWith(1, 3, expect.anything());
    });
    expect(services.departmentResponsesApi.adminEdit).not.toHaveBeenCalled();
    expect(services.transactionsApi.editResponse).not.toHaveBeenCalled();
  });

  it('independently edits each replied assignment row across three departments by its own assignment.id', async () => {
    const user = userEvent.setup();
    const adminAffairs = {
      ...sampleAssignment,
      id: 11,
      departmentName: 'الشؤون الإدارية',
      replyStatus: 'Replied',
      replySummary: 'إفادة الشؤون الإدارية',
      responseDate: '2026-07-07T00:00:00',
      departmentResponseId: null,
    };
    const finance = {
      ...sampleAssignment,
      id: 22,
      departmentName: 'المالية',
      replyStatus: 'Replied',
      replySummary: 'إفادة المالية',
      responseDate: '2026-07-07T00:00:00',
      departmentResponseId: null,
    };
    const hr = {
      ...sampleAssignment,
      id: 33,
      departmentName: 'الموارد البشرية',
      replyStatus: 'Replied',
      replySummary: 'إفادة الموارد البشرية',
      responseDate: '2026-07-07T00:00:00',
      departmentResponseId: null,
    };
    mockApi(services.transactionsApi.getWorkspace).mockResolvedValue({
      data: { ...defaultWorkspace, assignments: [adminAffairs, finance, hr] },
    });
    mockApi(services.transactionsApi.getAssignments).mockResolvedValue({
      data: [adminAffairs, finance, hr],
    });
    mockApi(services.transactionsApi.editAssignmentReply).mockResolvedValue({ data: finance });

    renderDetail();
    await waitForDetailsReady();
    const card = getAssignmentsCard();

    const buttons = within(card).getAllByRole('button', { name: 'تمت الإفادة - تعديل إفادة الإحالة' });
    expect(buttons).toHaveLength(3);

    const editRowAndSave = async (departmentName: string, updatedSummary: string) => {
      const row = within(card).getByText(departmentName).closest('tr')!;
      await user.click(within(row).getByRole('button', { name: 'تمت الإفادة - تعديل إفادة الإحالة' }));

      const panel = within(card).getByTestId('admin-edit-assignment-reply-form-panel');
      const summaryField = await within(panel).findByLabelText('ملخص الإفادة *');
      expect(summaryField).toHaveValue(`إفادة ${departmentName}`);
      await user.clear(summaryField);
      await user.type(summaryField, updatedSummary);
      await user.click(within(panel).getByRole('button', { name: 'حفظ التعديلات' }));
    };

    await editRowAndSave('المالية', 'إفادة المالية المحدثة');
    await waitFor(() => {
      expect(services.transactionsApi.editAssignmentReply).toHaveBeenLastCalledWith(1, 22, expect.anything());
    });

    await editRowAndSave('الشؤون الإدارية', 'إفادة الشؤون الإدارية المحدثة');
    await waitFor(() => {
      expect(services.transactionsApi.editAssignmentReply).toHaveBeenLastCalledWith(1, 11, expect.anything());
    });

    await editRowAndSave('الموارد البشرية', 'إفادة الموارد البشرية المحدثة');
    await waitFor(() => {
      expect(services.transactionsApi.editAssignmentReply).toHaveBeenLastCalledWith(1, 33, expect.anything());
    });

    expect(services.transactionsApi.editAssignmentReply).toHaveBeenCalledTimes(3);
  });

  it('lets Admin open and prefill the follow-up reply edit form from a real-world replied follow-up row', async () => {
    const user = userEvent.setup();
    const repliedFollowUp = {
      ...sampleFollowUp,
      followUpNumber: '١١١',
      replyStatus: 'Replied',
      replyDate: '2026-06-28T00:00:00',
      replySummary: 'jh',
    };
    mockApi(services.transactionsApi.getWorkspace).mockResolvedValue({
      data: { ...defaultWorkspace, followUps: [repliedFollowUp] },
    });
    mockApi(services.transactionsApi.getFollowUps).mockResolvedValue({ data: [repliedFollowUp] });

    renderDetail();
    await waitForDetailsReady();
    const card = getFollowUpsCard();

    const replyButton = within(card).getByRole('button', { name: 'تم الرد - تعديل الرد' });
    expect(replyButton).toHaveTextContent('تم الرد');
    expect(within(card).queryByRole('button', { name: 'تعديل الرد' })).not.toBeInTheDocument();

    await user.click(replyButton);

    const panel = within(card).getByTestId('admin-edit-followup-reply-form-panel');
    expect(within(panel).getByLabelText(/ملخص الرد/)).toHaveValue('jh');
  });

  it('renders read-only تم الرد for unauthorized users and hides the edit button', async () => {
    mockUseAuth.mockReturnValue({
      canEdit: false,
      canClose: false,
      isDepartmentUser: false,
      user: { fullName: 'قارئ', role: 'Reader' },
      logout: vi.fn(),
      login: vi.fn(),
      isAdmin: false,
    });
    const repliedFollowUp = {
      ...sampleFollowUp,
      followUpNumber: '١١١',
      replyStatus: 'Replied',
      replyDate: '2026-06-28T00:00:00',
      replySummary: 'jh',
    };
    mockApi(services.transactionsApi.getWorkspace).mockResolvedValue({
      data: { ...defaultWorkspace, followUps: [repliedFollowUp] },
    });
    mockApi(services.transactionsApi.getFollowUps).mockResolvedValue({ data: [repliedFollowUp] });

    renderDetail();
    await waitForDetailsReady();
    const card = getFollowUpsCard();

    expect(within(card).getByText('تم الرد')).toBeInTheDocument();
    expect(within(card).queryByRole('button', { name: 'تم الرد - تعديل الرد' })).not.toBeInTheDocument();
  });

  it('does not make a follow-up editable when marked Replied but no reply date or summary is saved', async () => {
    const emptyRepliedFollowUp = {
      ...sampleFollowUp,
      followUpNumber: '١١١',
      replyStatus: 'Replied',
      replyDate: undefined,
      replySummary: undefined,
    };
    mockApi(services.transactionsApi.getWorkspace).mockResolvedValue({
      data: { ...defaultWorkspace, followUps: [emptyRepliedFollowUp] },
    });
    mockApi(services.transactionsApi.getFollowUps).mockResolvedValue({ data: [emptyRepliedFollowUp] });

    renderDetail();
    await waitForDetailsReady();
    const card = getFollowUpsCard();

    expect(within(card).getByText('تم الرد')).toBeInTheDocument();
    expect(within(card).queryByRole('button', { name: 'تم الرد - تعديل الرد' })).not.toBeInTheDocument();
  });

  it.each(['Closed', 'Cancelled', 'Archived'])(
    'hides the follow-up reply edit affordance for %s transactions',
    async (status) => {
      const repliedFollowUp = {
        ...sampleFollowUp,
        followUpNumber: '١١١',
        replyStatus: 'Replied',
        replyDate: '2026-06-28T00:00:00',
        replySummary: 'jh',
      };
      mockApi(services.transactionsApi.getWorkspace).mockResolvedValue({
        data: { ...defaultWorkspace, transaction: { ...baseTx, status }, followUps: [repliedFollowUp] },
      });
      mockApi(services.transactionsApi.getFollowUps).mockResolvedValue({ data: [repliedFollowUp] });

      renderDetail();
      await waitForDetailsReady();
      const card = getFollowUpsCard();

      expect(within(card).getByText('تم الرد')).toBeInTheDocument();
      expect(within(card).queryByRole('button', { name: 'تم الرد - تعديل الرد' })).not.toBeInTheDocument();
    },
  );

  it('does not render a separate تعديل الإفادة button next to the department response badge', async () => {
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

    await within(card).findByRole('button', { name: 'تمت الإفادة - تعديل إفادة الإحالة' });
    expect(within(card).queryByRole('button', { name: 'تعديل الإفادة' })).not.toBeInTheDocument();
    expect(within(card).queryByText('تعديل الإفادة')).not.toBeInTheDocument();
  });

  it('lets Supervisor open response edit from completed department response status even when canClose is false', async () => {
    mockUseAuth.mockReturnValue({
      canEdit: true,
      canClose: false,
      isDepartmentUser: false,
      user: { fullName: 'مشرف', role: 'Supervisor' },
      logout: vi.fn(),
      login: vi.fn(),
      isAdmin: false,
    });
    const user = userEvent.setup();
    const repliedAssignment = {
      ...sampleAssignment,
      replyStatus: 'Replied',
      responseDate: '2026-01-05',
      replySummary: 'نص الإفادة',
      departmentResponseId: null,
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

    const responseButton = await within(card).findByRole('button', { name: 'تمت الإفادة - تعديل إفادة الإحالة' });
    await user.click(responseButton);

    expect(within(card).getByTestId('admin-edit-assignment-reply-form-panel')).toBeInTheDocument();
    expect(await screen.findByLabelText('ملخص الإفادة *')).toHaveValue('نص الإفادة');
  });

  it('opens the row-level department response edit form from الإحالات والردود even when tx.responseCompleted is false', async () => {
    const user = userEvent.setup();
    const repliedAssignment = {
      ...sampleAssignment,
      replyStatus: 'Replied',
      responseDate: '2026-01-05',
      replySummary: 'نص الإفادة',
      departmentResponseId: null,
    };
    expect(baseTx.responseCompleted).toBe(false);
    mockApi(services.transactionsApi.getWorkspace).mockResolvedValue({
      data: {
        ...defaultWorkspace,
        transaction: { ...baseTx, responseCompleted: false },
        assignments: [repliedAssignment],
      },
    });
    mockApi(services.transactionsApi.getAssignments).mockResolvedValue({
      data: [repliedAssignment],
    });

    renderDetail();
    await waitForDetailsReady();

    const referralsTable = screen.getByRole('region', { name: /الإحالات والردود/ });
    const completedResponseButton = within(referralsTable).getByRole('button', {
      name: 'تمت الإفادة - تعديل إفادة الإحالة',
    });

    await user.click(completedResponseButton);

    expect(within(referralsTable).getByRole('region', { name: 'تعديل إفادة الإحالة' })).toBeInTheDocument();
    expect(await screen.findByLabelText('ملخص الإفادة *')).toHaveValue('نص الإفادة');
  });

  it('scopes each row-level edit to its own department without affecting other rows', async () => {
    const user = userEvent.setup();
    const legalAssignment = {
      ...sampleAssignment,
      id: 11,
      departmentId: 1,
      departmentName: 'الشؤون القانونية',
      replyStatus: 'Replied',
      responseDate: '2026-01-05',
      replySummary: 'إفادة الشؤون القانونية',
      departmentResponseId: 200,
    };
    const adminAssignment = {
      ...sampleAssignment,
      id: 12,
      departmentId: 2,
      departmentName: 'الشؤون الإدارية',
      replyStatus: 'Replied',
      responseDate: '2026-01-06',
      replySummary: 'إفادة الشؤون الإدارية',
      departmentResponseId: 201,
    };
    mockApi(services.transactionsApi.getWorkspace).mockResolvedValue({
      data: {
        ...defaultWorkspace,
        assignments: [legalAssignment, adminAssignment],
      },
    });
    mockApi(services.transactionsApi.getAssignments).mockResolvedValue({
      data: [legalAssignment, adminAssignment],
    });

    renderDetail();
    await waitForDetailsReady();
    const card = getAssignmentsCard();

    const legalRow = within(card).getByText('الشؤون القانونية').closest('tr')!;
    const legalButton = within(legalRow).getByRole('button', { name: 'تمت الإفادة - تعديل إفادة الإحالة' });
    await user.click(legalButton);

    expect(await screen.findByLabelText('ملخص الإفادة *')).toHaveValue('إفادة الشؤون القانونية');

    await user.click(within(card).getByRole('button', { name: 'إغلاق النموذج' }));

    const adminRow = within(card).getByText('الشؤون الإدارية').closest('tr')!;
    const adminButton = within(adminRow).getByRole('button', { name: 'تمت الإفادة - تعديل إفادة الإحالة' });
    await user.click(adminButton);

    expect(await screen.findByLabelText('ملخص الإفادة *')).toHaveValue('إفادة الشؤون الإدارية');
  });

  it('shows the updated department response summary in the same row after a successful admin edit', async () => {
    const user = userEvent.setup();
    const repliedAssignment = {
      ...sampleAssignment,
      id: 3,
      replyStatus: 'Replied',
      responseDate: '2026-01-05',
      replySummary: 'نص الإفادة',
      departmentResponseId: null,
    };
    const updatedAssignment = {
      ...repliedAssignment,
      responseDate: '2026-01-12',
      replyDate: '2026-01-12',
      replySummary: 'نص محدث للإفادة',
    };
    mockApi(services.transactionsApi.getWorkspace)
      .mockResolvedValueOnce({ data: { ...defaultWorkspace, assignments: [repliedAssignment] } })
      .mockResolvedValue({ data: { ...defaultWorkspace, assignments: [updatedAssignment] } });
    mockApi(services.transactionsApi.getAssignments).mockResolvedValue({
      data: [repliedAssignment],
    });
    mockApi(services.transactionsApi.editAssignmentReply).mockResolvedValue({
      data: updatedAssignment,
    });

    renderDetail();
    await waitForDetailsReady();

    const responseButton = await within(getAssignmentsCard()).findByRole('button', { name: 'تمت الإفادة - تعديل إفادة الإحالة' });
    await user.click(responseButton);

    const panel = within(getAssignmentsCard()).getByTestId('admin-edit-assignment-reply-form-panel');
    const summaryField = await within(panel).findByLabelText('ملخص الإفادة *');
    await user.type(summaryField, ' إضافة');
    await user.click(within(panel).getByRole('button', { name: 'حفظ التعديلات' }));

    await waitFor(() => {
      expect(services.transactionsApi.editAssignmentReply).toHaveBeenCalledWith(1, 3, expect.anything());
    });
    expect(services.departmentResponsesApi.adminEdit).not.toHaveBeenCalled();
    expect(services.transactionsApi.editResponse).not.toHaveBeenCalled();
    await waitFor(() => {
      expect(within(getAssignmentsCard()).getByText('نص محدث للإفادة')).toBeInTheDocument();
    });
  });

  it.each(['Closed', 'Cancelled', 'Archived'])(
    'hides the department response edit affordance for %s transactions',
    async (status) => {
      const repliedAssignment = {
        ...sampleAssignment,
        replyStatus: 'Replied',
        responseDate: '2026-01-05',
        departmentResponseId: 100,
      };
      mockApi(services.transactionsApi.getWorkspace).mockResolvedValue({
        data: {
          ...defaultWorkspace,
          transaction: { ...baseTx, status },
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
      expect(within(card).queryByRole('button', { name: 'تمت الإفادة - تعديل إفادة الإحالة' })).not.toBeInTheDocument();
    },
  );

  it('still makes completed response status editable without departmentResponseId as long as reply data exists', async () => {
    const repliedAssignment = {
      ...sampleAssignment,
      replyStatus: 'Replied',
      responseDate: '2026-01-05',
      replySummary: 'إفادة بدون معرف رد إدارة',
      departmentResponseId: null,
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
    expect(within(card).getByRole('button', { name: 'تمت الإفادة - تعديل إفادة الإحالة' })).toBeInTheDocument();
  });

  it('does not make status editable when a replied row has no saved reply data at all', async () => {
    const emptyRepliedAssignment = {
      ...sampleAssignment,
      replyStatus: 'Replied',
      responseDate: undefined,
      replyDate: undefined,
      replySummary: undefined,
      departmentResponseId: null,
    };
    mockApi(services.transactionsApi.getWorkspace).mockResolvedValue({
      data: {
        ...defaultWorkspace,
        assignments: [emptyRepliedAssignment],
      },
    });
    mockApi(services.transactionsApi.getAssignments).mockResolvedValue({
      data: [emptyRepliedAssignment],
    });

    renderDetail();
    await waitForDetailsReady();
    const card = getAssignmentsCard();

    expect(within(card).getByText('تمت الإفادة')).toBeInTheDocument();
    expect(within(card).queryByRole('button', { name: 'تمت الإفادة - تعديل إفادة الإحالة' })).not.toBeInTheDocument();
  });

  it('shows an error when the workspace refresh after an assignment reply edit fails', async () => {
    const user = userEvent.setup();
    const repliedAssignment = {
      ...sampleAssignment,
      replyStatus: 'Replied',
      responseDate: '2026-01-05',
      replySummary: 'نص الإفادة',
      departmentResponseId: null,
    };
    mockApi(services.transactionsApi.getWorkspace)
      .mockResolvedValueOnce({
        data: { ...defaultWorkspace, assignments: [repliedAssignment] },
      })
      .mockRejectedValueOnce(new Error('network error'));
    mockApi(services.transactionsApi.getAssignments).mockResolvedValue({
      data: [repliedAssignment],
    });
    mockApi(services.transactionsApi.editAssignmentReply).mockResolvedValue({
      data: { ...repliedAssignment, replySummary: 'نص الإفادة إضافة' },
    });

    renderDetail();
    await waitForDetailsReady();
    const card = getAssignmentsCard();

    const responseButton = await within(card).findByRole('button', { name: 'تمت الإفادة - تعديل إفادة الإحالة' });
    await user.click(responseButton);

    expect(within(card).getByTestId('admin-edit-assignment-reply-form-panel')).toBeInTheDocument();
    const summaryField = await screen.findByLabelText('ملخص الإفادة *');
    await user.type(summaryField, ' إضافة');

    await user.click(screen.getByRole('button', { name: 'حفظ التعديلات' }));

    // loadWorkspace surfaces its own failure via the page-level error state; the
    // fix under test ensures handleEditAssignmentReplySuccess awaits that refresh
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
    expect(within(card).queryByRole('button', { name: 'تمت الإفادة - تعديل إفادة الإحالة' })).not.toBeInTheDocument();
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
    const editButton = within(responseCard).getByRole('button', { name: 'تمت الإفادة - تعديل الإفادة' });
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

  it('lets an authorized supervisor open the completed response for editing by clicking تمت الإفادة, even when canClose is false, prefilled with the existing values', async () => {
    mockUseAuth.mockReturnValue({
      canEdit: true,
      canClose: false,
      isDepartmentUser: false,
      user: { fullName: 'مشرف', role: 'Supervisor' },
      logout: vi.fn(),
      login: vi.fn(),
      isAdmin: false,
    });
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
    mockApi(services.transactionsApi.getWorkspace).mockResolvedValue({
      data: { ...defaultWorkspace, transaction: completedTx },
    });
    mockApi(services.transactionsApi.getBasic).mockResolvedValue({ data: completedTx });

    renderDetail();
    await waitForDetailsReady();

    const responseCard = getResponseCard();
    const editButton = within(responseCard).getByRole('button', { name: 'تمت الإفادة - تعديل الإفادة' });
    expect(editButton).toHaveTextContent('تمت الإفادة');

    await user.click(editButton);

    const panel = within(responseCard).getByTestId('admin-edit-transaction-response-form-panel');
    const summaryField = await within(panel).findByLabelText('ملخص الإفادة *');
    expect(summaryField).toHaveValue('ملخص أصلي');
    expect(within(panel).getByLabelText('رقم الصادر *')).toHaveValue('OUT-1');
  });

  it('closes the edit response form on cancel without saving or changing the displayed response', async () => {
    const user = userEvent.setup();
    const confirmSpy = vi.spyOn(globalThis, 'confirm').mockReturnValue(true);
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
    mockApi(services.transactionsApi.getWorkspace).mockResolvedValue({
      data: { ...defaultWorkspace, transaction: completedTx },
    });
    mockApi(services.transactionsApi.getBasic).mockResolvedValue({ data: completedTx });

    renderDetail();
    await waitForDetailsReady();

    const responseCard = getResponseCard();
    const editButton = within(responseCard).getByRole('button', { name: 'تمت الإفادة - تعديل الإفادة' });
    await user.click(editButton);

    const panel = within(responseCard).getByTestId('admin-edit-transaction-response-form-panel');
    const summaryField = await within(panel).findByLabelText('ملخص الإفادة *');
    await user.clear(summaryField);
    await user.type(summaryField, 'مسودة لن تُحفظ');
    await user.click(within(panel).getByRole('button', { name: 'إلغاء' }));

    expect(confirmSpy).toHaveBeenCalledTimes(1);
    expect(services.transactionsApi.editResponse).not.toHaveBeenCalled();
    expect(within(responseCard).queryByTestId('admin-edit-transaction-response-form-panel')).not.toBeInTheDocument();
    expect(within(responseCard).getByText('ملخص أصلي')).toBeInTheDocument();
    expect(within(responseCard).getByRole('button', { name: 'تمت الإفادة - تعديل الإفادة' })).toBeInTheDocument();
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
    expect(within(responseCard).queryByRole('button', { name: 'تمت الإفادة - تعديل الإفادة' })).not.toBeInTheDocument();
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
      expect(within(responseCard).queryByRole('button', { name: 'تمت الإفادة - تعديل الإفادة' })).not.toBeInTheDocument();
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

describe('TransactionDetailPage indicators, action grouping, card order, and navigation', () => {
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

  it('renders compact indicator boxes without long inline descriptions, with the explanation available via an accessible tooltip', async () => {
    renderDetail();
    await waitForDetailsReady();

    expect(document.querySelector('.metric-hint')).not.toBeInTheDocument();

    const incomingDateLabel = screen.getByText('تاريخ الوارد');
    const tile = incomingDateLabel.closest('.transaction-metric-tile');
    expect(tile).not.toBeNull();
    const tooltip = within(tile as HTMLElement).getByRole('tooltip', { hidden: true });
    expect(tooltip).toHaveTextContent('بداية عمر المعاملة وأيام الإنجاز');
    expect(tooltip).toHaveAttribute('id');
    const trigger = within(tile as HTMLElement).getByRole('button', { name: 'شرح مؤشر تاريخ الوارد' });
    expect(trigger).toHaveAttribute('aria-describedby', tooltip.getAttribute('id'));
  });

  it('includes حالة الإجراء الحالية as one of the transaction indicator tiles', async () => {
    renderDetail();
    await waitForDetailsReady();

    const statusTile = getCurrentStatusCard();
    expect(statusTile.closest('.transaction-metric-grid')).not.toBeNull();
    expect(within(statusTile).getByText('حالة الإجراء الحالية')).toBeInTheDocument();
    expect(within(statusTile).getByText('لا تتطلب إفادة')).toBeInTheDocument();
  });

  it('groups the recurring and admin-date-correction actions beside تعديل in the action bar', async () => {
    renderDetail();
    await waitForDetailsReady();

    const bar = getActionBar();
    expect(within(bar).getByRole('link', { name: 'تعديل' })).toBeInTheDocument();
    expect(within(bar).getByRole('button', { name: 'تفعيل متابعة دورية لهذه المعاملة' })).toBeInTheDocument();
    expect(within(bar).getByRole('button', { name: 'تصحيح التواريخ إداريًا' })).toBeInTheDocument();
  });

  it('orders التعقيبات والردود before الإفادة in the section grid', async () => {
    renderDetail();
    await waitForDetailsReady();

    const sections = Array.from(document.querySelectorAll('.transaction-sections-grid > section'))
      .map((section) => section.getAttribute('aria-label'));
    const followUpsIndex = sections.indexOf('التعقيبات والردود');
    const responseIndex = sections.indexOf('الإفادة');
    expect(followUpsIndex).toBeGreaterThan(-1);
    expect(responseIndex).toBeGreaterThan(-1);
    expect(followUpsIndex).toBeLessThan(responseIndex);
  });

  it('disables previous/next transaction navigation when no adjacent transaction exists', async () => {
    mockApi(services.transactionsApi.getAdjacent).mockResolvedValue({ data: { previousId: null, nextId: null } });

    renderDetail();
    await waitForDetailsReady();

    await waitFor(() => {
      expect(screen.getByRole('button', { name: /المعاملة السابقة/ })).toBeDisabled();
    });
    expect(screen.getByRole('button', { name: /المعاملة التالية/ })).toBeDisabled();
  });

  it('navigates to the next transaction when an adjacent id is available', async () => {
    const user = userEvent.setup();
    mockApi(services.transactionsApi.getAdjacent).mockResolvedValue({ data: { previousId: 5, nextId: 9 } });

    renderDetail();
    await waitForDetailsReady();

    const nextButton = await screen.findByRole('button', { name: /المعاملة التالية/ });
    await waitFor(() => expect(nextButton).not.toBeDisabled());

    const callsBefore = mockApi(services.transactionsApi.getWorkspace).mock.calls.length;
    await user.click(nextButton);

    await waitFor(() => {
      expect(mockApi(services.transactionsApi.getWorkspace).mock.calls.length).toBeGreaterThan(callsBefore);
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

  it('shows a procedural-completion banner, not a closed status, when all department referrals have replied', async () => {
    const procedurallyCompleteTransaction = {
      ...baseTx,
      requiresResponse: true,
      responseCompleted: false,
      status: 'ReadyForResponse',
      isProcedurallyCompleteForReporting: true,
      proceduralCompletionDateForReporting: '2026-07-09',
    };
    mockApi(services.transactionsApi.getWorkspace).mockResolvedValue({
      data: { ...defaultWorkspace, transaction: procedurallyCompleteTransaction },
    });
    mockApi(services.transactionsApi.getBasic).mockResolvedValue({ data: procedurallyCompleteTransaction });
    renderDetail();
    await waitForDetailsReady();

    expect(screen.getByText('المعاملة مكتملة إجرائيًا وتنتظر اعتماد الإفادة النهائية.')).toBeInTheDocument();
    expect(screen.queryByText('مغلقة')).not.toBeInTheDocument();
  });

  it('does not show the procedural-completion banner once the final response is registered', async () => {
    const respondedTransaction = {
      ...baseTx,
      requiresResponse: true,
      responseCompleted: true,
      isProcedurallyCompleteForReporting: true,
      proceduralCompletionDateForReporting: '2026-07-09',
    };
    mockApi(services.transactionsApi.getWorkspace).mockResolvedValue({
      data: { ...defaultWorkspace, transaction: respondedTransaction },
    });
    mockApi(services.transactionsApi.getBasic).mockResolvedValue({ data: respondedTransaction });
    renderDetail();
    await waitForDetailsReady();

    expect(screen.queryByText('المعاملة مكتملة إجرائيًا وتنتظر اعتماد الإفادة النهائية.')).not.toBeInTheDocument();
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
    await user.type(within(panel).getByLabelText('تاريخ الإفادة *'), '16/01/1448');
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
