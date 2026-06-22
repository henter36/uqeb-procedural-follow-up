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

function getAssignmentsCard() {
  return screen.getByRole('region', { name: 'التحويلات والردود' });
}

function getFollowUpsCard() {
  return screen.getByRole('region', { name: 'التعقيبات والردود' });
}

function getAttachmentsCard() {
  return screen.getByRole('region', { name: 'المرفقات' });
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
  vi.mocked(services.transactionsApi.getBasic).mockResolvedValue({ data: baseTx } as never);
  vi.mocked(services.transactionsApi.getAssignments).mockResolvedValue({ data: [sampleAssignment] } as never);
  vi.mocked(services.transactionsApi.getFollowUps).mockResolvedValue({ data: [sampleFollowUp] } as never);
  vi.mocked(services.transactionsApi.getAttachments).mockResolvedValue({ data: [sampleAttachment] } as never);
  vi.mocked(services.transactionsApi.getAuditLog).mockResolvedValue({
    data: { items: [{ id: 1, action: 'Create', userName: 'مختبر', createdAt: '2026-01-01' }], hasNextPage: false },
  } as never);
  vi.mocked(services.transactionsApi.getFollowUpDepartments).mockResolvedValue({
    data: [{ departmentId: 2, departmentName: 'إدارة ثانية', isDefaultSelected: true }],
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
      expect(getAssignmentsCard()).toBeInTheDocument();
      expect(getFollowUpsCard()).toBeInTheDocument();
      expect(getAttachmentsCard()).toBeInTheDocument();
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
      expect(getAssignmentsCard()).toBeInTheDocument();
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

  it('refreshes only assignments card after adding assignment from card button', async () => {
    const user = userEvent.setup();
    vi.mocked(services.transactionsApi.addAssignment).mockResolvedValue({ data: { id: 99 } } as never);
    vi.mocked(services.transactionsApi.getAssignments)
      .mockResolvedValueOnce({ data: [] } as never)
      .mockResolvedValueOnce({ data: [sampleAssignment] } as never);

    renderDetail();
    await waitForDetailsReady();
    const card = getAssignmentsCard();
    await waitFor(() => expect(within(card).getByRole('button', { name: '+ إضافة تحويل' })).toBeInTheDocument());

    const callsBefore = vi.mocked(services.transactionsApi.getFollowUps).mock.calls.length;
    await user.click(within(card).getByRole('button', { name: '+ إضافة تحويل' }));
    expect(within(card).getByTestId('assignment-form-panel')).toBeInTheDocument();

    await user.selectOptions(screen.getByLabelText('الإدارة *'), '2');
    await user.type(screen.getByLabelText('الإجراء المطلوب'), 'مراجعة');
    await user.click(screen.getByRole('button', { name: 'حفظ التحويل' }));

    await waitFor(() => {
      expect(screen.getByRole('status')).toHaveTextContent('تم إضافة التحويل بنجاح');
      expect(screen.getByText('مراجعة')).toBeInTheDocument();
      expect(within(card).queryByTestId('assignment-form-panel')).not.toBeInTheDocument();
    });
    expect(services.transactionsApi.addAssignment).toHaveBeenCalledTimes(1);
    expect(vi.mocked(services.transactionsApi.getAssignments).mock.calls.length).toBeGreaterThan(1);
    expect(vi.mocked(services.transactionsApi.getFollowUps).mock.calls).toHaveLength(callsBefore);
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

    const attachmentsCard = getAttachmentsCard();
    await user.click(within(attachmentsCard).getByRole('button', { name: 'إعادة المحاولة' }));

    await waitFor(() => {
      expect(screen.getByText('لا توجد مرفقات لهذه المعاملة.')).toBeInTheDocument();
    });
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
    vi.mocked(services.transactionsApi.getAssignments).mockResolvedValue({ data: [] } as never);
    vi.mocked(services.transactionsApi.getFollowUps).mockResolvedValue({ data: [] } as never);
    vi.mocked(services.transactionsApi.getAttachments).mockResolvedValue({ data: [] } as never);
  });

  afterEach(() => {
    cleanup();
  });

  it('opens assignment form inside assignments card from card header button', async () => {
    const user = userEvent.setup();
    renderDetail();
    await waitForDetailsReady();
    const card = getAssignmentsCard();
    await waitFor(() => expect(within(card).getByRole('button', { name: '+ إضافة تحويل' })).toBeInTheDocument());

    const addButton = within(card).getByRole('button', { name: '+ إضافة تحويل' });
    expect(addButton).toHaveAttribute('type', 'button');

    await user.click(addButton);

    const panel = within(card).getByTestId('assignment-form-panel');
    expect(panel).toBeInTheDocument();
    expect(within(panel).getByLabelText('الإدارة *')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'حفظ التحويل' })).toBeInTheDocument();
    expect(screen.queryByTestId('followup-form-panel')).not.toBeInTheDocument();
    expect(screen.queryByTestId('attachment-form-panel')).not.toBeInTheDocument();
  });

  it('saves assignment from card form and refreshes data', async () => {
    const user = userEvent.setup();
    vi.mocked(services.transactionsApi.addAssignment).mockResolvedValue({ data: { id: 9 } } as never);
    vi.mocked(services.transactionsApi.getAssignments)
      .mockResolvedValueOnce({ data: [] } as never)
      .mockResolvedValue({ data: [sampleAssignment] } as never);

    renderDetail();
    await waitForDetailsReady();
    const card = getAssignmentsCard();
    await waitFor(() => expect(within(card).getByRole('button', { name: '+ إضافة تحويل' })).toBeInTheDocument());

    await user.click(within(card).getByRole('button', { name: '+ إضافة تحويل' }));
    await user.selectOptions(screen.getByLabelText('الإدارة *'), '2');
    await user.type(screen.getByLabelText('الإجراء المطلوب'), 'مراجعة');
    await user.click(screen.getByRole('button', { name: 'حفظ التحويل' }));

    await waitFor(() => {
      expect(screen.getByRole('status')).toHaveTextContent('تم إضافة التحويل بنجاح');
      expect(within(card).queryByTestId('assignment-form-panel')).not.toBeInTheDocument();
    });
    expect(services.transactionsApi.addAssignment).toHaveBeenCalledTimes(1);
    expect(vi.mocked(services.transactionsApi.getAssignments).mock.calls.length).toBeGreaterThan(1);
    expect(vi.mocked(services.transactionsApi.getBasic).mock.calls.length).toBeGreaterThan(1);
  });

  it('keeps assignment form open when save fails', async () => {
    const user = userEvent.setup();
    vi.mocked(services.transactionsApi.addAssignment).mockRejectedValue(new Error('save failed'));

    renderDetail();
    await waitForDetailsReady();
    const card = getAssignmentsCard();
    await waitFor(() => expect(within(card).getByRole('button', { name: '+ إضافة تحويل' })).toBeInTheDocument());

    await user.click(within(card).getByRole('button', { name: '+ إضافة تحويل' }));
    await user.selectOptions(screen.getByLabelText('الإدارة *'), '2');
    await user.click(screen.getByRole('button', { name: 'حفظ التحويل' }));

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
    await waitFor(() => expect(within(card).getByRole('button', { name: '+ إضافة تحويل' })).toBeInTheDocument());

    const toggle = within(card).getByRole('button', { name: '+ إضافة تحويل' });
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
    await waitFor(() => expect(within(card).getByRole('button', { name: '+ إضافة تحويل' })).toBeInTheDocument());

    await user.click(within(card).getByRole('button', { name: '+ إضافة تحويل' }));
    await user.type(screen.getByLabelText('الإجراء المطلوب'), 'تعديل');
    await user.click(within(card).getByRole('button', { name: '+ إضافة تحويل' }));

    expect(confirmSpy).toHaveBeenCalledTimes(1);
    expect(within(card).queryByTestId('assignment-form-panel')).not.toBeInTheDocument();
  });

  it('closes assignment form from panel header without confirm after successful save', async () => {
    const user = userEvent.setup();
    const confirmSpy = vi.spyOn(globalThis, 'confirm');
    vi.mocked(services.transactionsApi.addAssignment).mockResolvedValue({ data: { id: 9 } } as never);

    renderDetail();
    await waitForDetailsReady();
    const card = getAssignmentsCard();
    await waitFor(() => expect(within(card).getByRole('button', { name: '+ إضافة تحويل' })).toBeInTheDocument());

    await user.click(within(card).getByRole('button', { name: '+ إضافة تحويل' }));
    await user.selectOptions(screen.getByLabelText('الإدارة *'), '2');
    await user.click(screen.getByRole('button', { name: 'حفظ التحويل' }));

    await waitFor(() => expect(within(card).queryByTestId('assignment-form-panel')).not.toBeInTheDocument());
    expect(confirmSpy).not.toHaveBeenCalled();
  });

  it('closes assignment form via panel close button with dirty confirm', async () => {
    const user = userEvent.setup();
    const confirmSpy = vi.spyOn(globalThis, 'confirm').mockReturnValue(true);

    renderDetail();
    await waitForDetailsReady();
    const card = getAssignmentsCard();
    await waitFor(() => expect(within(card).getByRole('button', { name: '+ إضافة تحويل' })).toBeInTheDocument());

    await user.click(within(card).getByRole('button', { name: '+ إضافة تحويل' }));
    const panel = within(card).getByTestId('assignment-form-panel');
    await user.type(screen.getByLabelText('الإجراء المطلوب'), 'مسودة');
    await user.click(within(panel).getByRole('button', { name: 'إغلاق النموذج' }));

    expect(confirmSpy).toHaveBeenCalledTimes(1);
    expect(within(card).queryByTestId('assignment-form-panel')).not.toBeInTheDocument();
  });

  it('opens follow-up form inside follow-ups card and saves', async () => {
    const user = userEvent.setup();
    vi.mocked(services.transactionsApi.addFollowUp).mockResolvedValue({ data: { id: 21 } } as never);
    vi.mocked(services.transactionsApi.getFollowUps)
      .mockResolvedValueOnce({ data: [] } as never)
      .mockResolvedValue({ data: [sampleFollowUp] } as never);

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
    expect(vi.mocked(services.transactionsApi.getFollowUps).mock.calls.length).toBeGreaterThan(1);
    expect(vi.mocked(services.transactionsApi.getBasic).mock.calls.length).toBeGreaterThan(1);
  });

  it('opens attachment form inside attachments card and uploads file', async () => {
    const user = userEvent.setup();
    vi.mocked(services.transactionsApi.uploadAttachment).mockResolvedValue({ data: sampleAttachment } as never);
    vi.mocked(services.transactionsApi.getAttachments)
      .mockResolvedValueOnce({ data: [] } as never)
      .mockResolvedValue({ data: [sampleAttachment] } as never);

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
    expect(vi.mocked(services.transactionsApi.getAttachments).mock.calls.length).toBeGreaterThan(1);
  });

  it('shows only one inline add form at a time when switching cards', async () => {
    const user = userEvent.setup();
    renderDetail();
    await waitForDetailsReady();

    await user.click(within(getAssignmentsCard()).getByRole('button', { name: '+ إضافة تحويل' }));
    expect(screen.getByTestId('assignment-form-panel')).toBeInTheDocument();

    await user.click(within(getFollowUpsCard()).getByRole('button', { name: '+ إضافة تعقيب' }));
    expect(screen.queryByTestId('assignment-form-panel')).not.toBeInTheDocument();
    expect(within(getFollowUpsCard()).getByTestId('followup-form-panel')).toBeInTheDocument();
  });

  it('keeps open assignment form after assignments list finishes loading', async () => {
    const user = userEvent.setup();
    let resolveAssignments: ((value: never) => void) | undefined;
    vi.mocked(services.transactionsApi.getAssignments).mockImplementation(
      () => new Promise((resolve) => { resolveAssignments = resolve; }),
    );

    renderDetail();
    await waitForDetailsReady();
    const card = getAssignmentsCard();
    await waitFor(() => expect(within(card).getByRole('button', { name: '+ إضافة تحويل' })).toBeInTheDocument());

    await user.click(within(card).getByRole('button', { name: '+ إضافة تحويل' }));
    expect(within(card).getByTestId('assignment-form-panel')).toBeInTheDocument();

    resolveAssignments!({ data: [] } as never);
    await waitFor(() => expect(screen.getByText('لا توجد تحويلات مسجلة لهذه المعاملة.')).toBeInTheDocument());
    expect(within(card).getByTestId('assignment-form-panel')).toBeInTheDocument();
  });

  it('opens reply form inside assignments card for pending assignment', async () => {
    const user = userEvent.setup();
    vi.mocked(services.transactionsApi.getAssignments).mockResolvedValue({ data: [sampleAssignment] } as never);
    vi.mocked(services.transactionsApi.replyAssignment).mockResolvedValue({ data: {} } as never);

    renderDetail();
    await waitForDetailsReady();
    const card = getAssignmentsCard();
    await waitFor(() => expect(within(card).getByRole('button', { name: 'تسجيل رد' })).toBeInTheDocument());

    await user.click(within(card).getByRole('button', { name: 'تسجيل رد' }));
    expect(within(card).getByTestId('reply-assignment-form-panel')).toBeInTheDocument();

    await user.type(screen.getByLabelText(/ملخص الرد/), 'تم الرد');
    await user.click(screen.getByRole('button', { name: 'حفظ الرد' }));

    await waitFor(() => {
      expect(screen.getByRole('status')).toHaveTextContent('تم تسجيل الرد بنجاح');
      expect(within(card).queryByTestId('reply-assignment-form-panel')).not.toBeInTheDocument();
    });
    expect(services.transactionsApi.replyAssignment).toHaveBeenCalledWith(1, 10, expect.any(Object));
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

  it('loads transaction data automatically and shows hero summary', async () => {
    renderDetail();

    await waitFor(() => {
      expect(screen.getByRole('heading', { level: 2, name: 'IN-1' })).toBeInTheDocument();
    });
    expect(screen.getByRole('navigation', { name: 'إجراءات المعاملة' })).toBeInTheDocument();
    expect(getActionBarButton('إضافة تحويل')).toBeInTheDocument();
    expect(screen.getByText('منذ ورود المعاملة')).toBeInTheDocument();
  });

  it('opens inline assignment form from action bar in hero area', async () => {
    const user = userEvent.setup();
    renderDetail();

    await waitFor(() => {
      expect(getActionBarButton('إضافة تحويل')).toBeInTheDocument();
    });

    await user.click(getActionBarButton('إضافة تحويل'));

    expect(within(getAssignmentsCard()).getByTestId('assignment-form-panel')).toBeInTheDocument();
    expect(screen.getByLabelText('الإدارة *')).toBeInTheDocument();
  });

  it('does not confirm after successful assignment save from action bar', async () => {
    const user = userEvent.setup();
    const confirmSpy = vi.spyOn(globalThis, 'confirm');
    vi.mocked(services.transactionsApi.addAssignment).mockResolvedValue({ data: { id: 9 } } as never);

    renderDetail();
    await waitFor(() => expect(getActionBarButton('إضافة تحويل')).toBeInTheDocument());

    await user.click(getActionBarButton('إضافة تحويل'));
    await user.selectOptions(screen.getByLabelText('الإدارة *'), '2');
    await user.type(screen.getByLabelText('الإجراء المطلوب'), 'مراجعة');
    await user.click(screen.getByRole('button', { name: 'حفظ التحويل' }));

    await waitFor(() => {
      expect(screen.getByRole('status')).toHaveTextContent('تم إضافة التحويل بنجاح');
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
    cleanup();
  });

  it('hides mutation actions for reader role across cards and action bar', async () => {
    renderDetail();

    await waitFor(() => expect(screen.getByRole('heading', { level: 2, name: 'IN-1' })).toBeInTheDocument());
    expect(screen.queryByRole('button', { name: 'إضافة تحويل' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '+ إضافة تحويل' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'إضافة تعقيب' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '+ إضافة مرفق' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'إضافة أول تحويل' })).not.toBeInTheDocument();
  });

  it('hides admin mutation actions for department user but allows reply', async () => {
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
    expect(screen.queryByRole('button', { name: '+ إضافة تحويل' })).not.toBeInTheDocument();
    expect(screen.queryByRole('button', { name: '+ إضافة تعقيب' })).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: 'تعديل' })).not.toBeInTheDocument();
    expect(within(getAssignmentsCard()).getByRole('button', { name: 'تسجيل رد' })).toBeInTheDocument();
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
    vi.mocked(services.transactionsApi.getAssignments).mockResolvedValue({ data: [] } as never);

    renderDetail();

    await waitFor(() => {
      expect(within(getAssignmentsCard()).getByRole('button', { name: '+ إضافة تحويل' })).toBeInTheDocument();
      expect(within(getFollowUpsCard()).getByRole('button', { name: '+ إضافة تعقيب' })).toBeInTheDocument();
      expect(within(getAttachmentsCard()).getByRole('button', { name: '+ إضافة مرفق' })).toBeInTheDocument();
    });
  });
});
