import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import * as services from '../api/services';
import DataQualityPage from './DataQualityPage';
import type { DataQualitySummary } from '../api/types';

vi.mock('../api/services', () => ({
  dataQualityApi: {
    getSummary: vi.fn(),
    markReviewed: vi.fn(),
    unmarkReviewed: vi.fn(),
  },
}));

const navigate = vi.fn();

vi.mock('react-router-dom', async (importOriginal) => ({
  ...await importOriginal<typeof import('react-router-dom')>(),
  useNavigate: () => navigate,
}));

const summary: DataQualitySummary = {
  totalIssues: 3,
  criticalCount: 1,
  highCount: 1,
  mediumCount: 1,
  lowCount: 0,
  affectedTransactions: 2,
  generatedAtUtc: '2026-07-08T09:00:00Z',
  issues: [
    {
      id: 'tx:1:overdue-duration',
      issueKey: 'tx:1:overdue-duration',
      ruleCode: 'OverdueDurationExceedsThreshold',
      severity: 4,
      severityLabel: 'حرجة',
      category: 'التأخر',
      issueType: 'مدة التأخر تتجاوز الحد المحدد',
      transactionId: 1,
      trackingNumber: 'TRK-001',
      incomingNumber: 'IN-001',
      subject: 'معاملة متأخرة',
      departmentName: 'الشؤون المالية',
      fieldName: 'ResponseDueDate',
      currentValue: '20 يوم تأخر',
      daysValue: 20,
      impact: 'أثر التأخر',
      suggestedAction: 'فتح المعاملة',
      isReviewed: false,
    },
    {
      id: 'tx:2:assignment:3:referral-after-incoming',
      issueKey: 'tx:2:assignment:3:referral-after-incoming',
      ruleCode: 'ReferralDateAfterIncomingDate',
      severity: 2,
      severityLabel: 'متوسطة',
      category: 'الإحالات',
      issueType: 'تاريخ الإحالة أكبر من تاريخ الوارد',
      transactionId: 2,
      trackingNumber: 'TRK-002',
      incomingNumber: 'IN-002',
      subject: 'إحالة لاحقة',
      departmentName: 'الموارد البشرية',
      fieldName: 'AssignedDate',
      currentValue: 'تاريخ الإحالة بعد تاريخ الوارد بـ 1 يوم',
      daysValue: 1,
      impact: 'أثر الإحالة',
      suggestedAction: 'مراجعة تاريخ الإحالة',
      isReviewed: false,
    },
    {
      id: 'tx:3:short-response-period',
      issueKey: 'tx:3:short-response-period',
      ruleCode: 'ResponsePeriodLessThanThreshold',
      severity: 2,
      severityLabel: 'متوسطة',
      category: 'فترة الرد',
      issueType: 'فترة الرد أقل من الحد المحدد',
      transactionId: 3,
      trackingNumber: 'TRK-003',
      incomingNumber: 'IN-003',
      subject: 'فترة قصيرة',
      fieldName: 'ResponseDueDate',
      currentValue: 'فترة الرد المحددة 2 يوم',
      daysValue: 2,
      impact: 'أثر فترة الرد',
      suggestedAction: 'مراجعة تاريخ الاستحقاق',
      isReviewed: true,
    },
  ],
};

const duplicateSummary: DataQualitySummary = {
  totalIssues: 1,
  criticalCount: 0,
  highCount: 1,
  mediumCount: 0,
  lowCount: 0,
  affectedTransactions: 2,
  generatedAtUtc: '2026-07-08T09:00:00Z',
  issues: [
    {
      id: 'tx-pair:1:2:duplicate-similar',
      issueKey: 'tx-pair:1:2:duplicate-similar',
      ruleCode: 'PotentialDuplicateOrSimilarTransaction',
      severity: 3,
      severityLabel: 'عالية',
      category: 'التكرار والتشابه',
      issueType: 'معاملات مكررة أو متشابهة',
      transactionId: 1,
      trackingNumber: 'TRK-001',
      incomingNumber: 'و/١٤٤٧/001',
      subject: 'طلب اعتماد صرف',
      relatedTransactionId: 2,
      relatedTrackingNumber: 'TRK-002',
      relatedIncomingNumber: 'و-1447-001',
      relatedIncomingDate: '2026-07-01T00:00:00',
      similarityReason: 'نفس رقم الوارد والتاريخ والجهة',
      similarityScore: 1,
      departmentName: 'وزارة المالية',
      fieldName: 'IncomingNumber/IncomingDate/IncomingParty/Subject',
      currentValue: 'و/١٤٤٧/001 (2026-07-01) ↔ و-1447-001 (2026-07-01)',
      daysValue: 0,
      impact: 'المعاملة TRK-001 قد تتشابه مع TRK-002',
      suggestedAction: 'فتح المعاملتين ومراجعة ما إذا كانتا تمثلان نفس الطلب.',
      isReviewed: false,
    },
  ],
};

function mockSummary(data: DataQualitySummary = summary) {
  vi.mocked(services.dataQualityApi.getSummary).mockResolvedValue(
    { data } as Awaited<ReturnType<typeof services.dataQualityApi.getSummary>>,
  );
  vi.mocked(services.dataQualityApi.markReviewed).mockResolvedValue(
    {} as Awaited<ReturnType<typeof services.dataQualityApi.markReviewed>>,
  );
  vi.mocked(services.dataQualityApi.unmarkReviewed).mockResolvedValue(
    {} as Awaited<ReturnType<typeof services.dataQualityApi.unmarkReviewed>>,
  );
}

function renderPage() {
  return render(
    <MemoryRouter>
      <DataQualityPage />
    </MemoryRouter>,
  );
}

async function applyOverdueRule(user: ReturnType<typeof userEvent.setup>) {
  await screen.findByText('اختر قاعدة جودة بيانات للبدء');
  await user.type(screen.getAllByPlaceholderText('عدد الأيام')[0], '10');
  await user.click(screen.getByRole('button', { name: 'تطبيق الفلاتر' }));
}

describe('DataQualityPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    navigate.mockReset();
    mockSummary();
  });

  afterEach(() => {
    cleanup();
  });

  it('loads the page and displays summary cards and issue types', async () => {
    const user = userEvent.setup();
    renderPage();

    expect(await screen.findByText('جودة البيانات')).toBeInTheDocument();
    expect(screen.getByText('الفلاتر وقواعد الاكتشاف')).toBeInTheDocument();
    expect(screen.getByText('النتائج')).toBeInTheDocument();
    expect(screen.getByText('معاملات مكررة أو متشابهة')).toBeInTheDocument();
    expect(screen.getByText('إجمالي الملاحظات')).toBeInTheDocument();
    expect(screen.getByText('معاملات متأثرة')).toBeInTheDocument();
    await applyOverdueRule(user);
    expect(screen.getByText('مدة التأخر تتجاوز الحد المحدد')).toBeInTheDocument();
    expect(screen.getAllByText('تاريخ الإحالة أكبر من تاريخ الوارد')).toHaveLength(2);
    expect(screen.getByText('فترة الرد أقل من الحد المحدد')).toBeInTheDocument();
  });

  it('sends selected rule filters to the API', async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText('اختر قاعدة جودة بيانات للبدء');

    const dayInputs = screen.getAllByPlaceholderText('عدد الأيام');
    await user.type(dayInputs[0], '10');
    await user.click(screen.getByLabelText('عرض المعاملات التي تاريخ الإحالة فيها أكبر من تاريخ الوارد'));
    await user.type(dayInputs[1], '5');
    await user.click(screen.getByLabelText('عرض معاملات مكررة أو متشابهة'));
    await user.click(screen.getByRole('button', { name: 'تطبيق الفلاتر' }));

    await waitFor(() => expect(services.dataQualityApi.getSummary).toHaveBeenLastCalledWith(
      expect.objectContaining({
        overdueMoreThanDays: 10,
        includeReferralDateAfterIncomingDate: true,
        responsePeriodLessThanDays: 5,
        includePotentialDuplicateTransactions: true,
      }),
    ));
  });

  it('maps review filter values to API parameters', async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText('اختر قاعدة جودة بيانات للبدء');

    await user.selectOptions(screen.getByLabelText('حالة المراجعة'), 'all');
    await user.click(screen.getByRole('button', { name: 'تطبيق الفلاتر' }));
    await waitFor(() => expect(services.dataQualityApi.getSummary).toHaveBeenLastCalledWith(
      expect.objectContaining({ includeReviewed: true }),
    ));

    await user.selectOptions(screen.getByLabelText('حالة المراجعة'), 'reviewed');
    await user.click(screen.getByRole('button', { name: 'تطبيق الفلاتر' }));
    await waitFor(() => expect(services.dataQualityApi.getSummary).toHaveBeenLastCalledWith(
      expect.objectContaining({ reviewedOnly: true }),
    ));
  });

  it('marks and unmarks issues then reloads results', async () => {
    const user = userEvent.setup();
    renderPage();
    await applyOverdueRule(user);
    await waitFor(() => expect(services.dataQualityApi.getSummary).toHaveBeenLastCalledWith(
      expect.objectContaining({ overdueMoreThanDays: 10 }),
    ));

    const dayInputs = screen.getAllByPlaceholderText('عدد الأيام');
    await user.clear(dayInputs[0]);
    await user.type(dayInputs[0], '20');

    await user.click(screen.getAllByRole('button', { name: 'تعليم كمراجعة' })[0]);
    await waitFor(() => expect(services.dataQualityApi.markReviewed).toHaveBeenCalledWith({
      issueKey: 'tx:1:overdue-duration',
      transactionId: 1,
      ruleCode: 'OverdueDurationExceedsThreshold',
    }));
    expect(services.dataQualityApi.getSummary).toHaveBeenLastCalledWith(
      expect.objectContaining({ overdueMoreThanDays: 10 }),
    );
    expect(screen.getByText('تمت مراجعة هذه الملاحظة، ولن تظهر في النتائج الافتراضية القادمة.')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'إزالة المراجعة' }));
    await waitFor(() => expect(services.dataQualityApi.unmarkReviewed).toHaveBeenCalledWith({
      issueKey: 'tx:3:short-response-period',
    }));
    expect(services.dataQualityApi.getSummary).toHaveBeenLastCalledWith(
      expect.objectContaining({ overdueMoreThanDays: 10 }),
    );
    expect(screen.getByText('تمت إزالة علامة المراجعة، وستعود الملاحظة للظهور إذا بقيت القاعدة منطبقة.')).toBeInTheDocument();
    expect(services.dataQualityApi.getSummary).toHaveBeenCalledTimes(4);
  });

  it('shows an error without success feedback when marking an issue fails', async () => {
    vi.mocked(services.dataQualityApi.markReviewed).mockRejectedValueOnce(new Error('failed'));
    const user = userEvent.setup();
    renderPage();
    await applyOverdueRule(user);

    await user.click(screen.getAllByRole('button', { name: 'تعليم كمراجعة' })[0]);

    expect(await screen.findByText('تعذر تعليم الملاحظة كمراجعة.')).toBeInTheDocument();
    expect(screen.queryByText('تمت مراجعة هذه الملاحظة، ولن تظهر في النتائج الافتراضية القادمة.')).not.toBeInTheDocument();
    expect(services.dataQualityApi.getSummary).toHaveBeenCalledTimes(2);
  });

  it('shows an error without success feedback when unmarking an issue fails', async () => {
    vi.mocked(services.dataQualityApi.unmarkReviewed).mockRejectedValueOnce(new Error('failed'));
    const user = userEvent.setup();
    renderPage();
    await applyOverdueRule(user);

    await user.click(screen.getByRole('button', { name: 'إزالة المراجعة' }));

    expect(await screen.findByText('تعذر إزالة علامة المراجعة.')).toBeInTheDocument();
    expect(screen.queryByText('تمت إزالة علامة المراجعة، وستعود الملاحظة للظهور إذا بقيت القاعدة منطبقة.')).not.toBeInTheDocument();
    expect(services.dataQualityApi.getSummary).toHaveBeenCalledTimes(2);
  });

  it('opens the existing transaction details page', async () => {
    const user = userEvent.setup();
    renderPage();
    await applyOverdueRule(user);

    await user.click(screen.getAllByRole('button', { name: 'فتح المعاملة' })[0]);

    expect(navigate).toHaveBeenCalledWith('/transactions/1');
  });

  it('opens both transactions for a potential duplicate pair', async () => {
    mockSummary(duplicateSummary);
    const user = userEvent.setup();
    renderPage();

    await screen.findByText('اختر قاعدة جودة بيانات للبدء');
    await user.click(screen.getByLabelText('عرض معاملات مكررة أو متشابهة'));
    await user.click(screen.getByRole('button', { name: 'تطبيق الفلاتر' }));

    expect(await screen.findByText('نفس رقم الوارد والتاريخ والجهة')).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: 'فتح المعاملة الأولى' }));
    expect(navigate).toHaveBeenCalledWith('/transactions/1');

    await user.click(screen.getByRole('button', { name: 'فتح المعاملة المشابهة' }));
    expect(navigate).toHaveBeenCalledWith('/transactions/2');
  });

  it('shows a guided empty state before any rule is applied', async () => {
    mockSummary({ ...summary, totalIssues: 0, issues: [] });

    renderPage();

    expect(await screen.findByText('اختر قاعدة جودة بيانات للبدء')).toBeInTheDocument();
  });

  it('shows an empty state when no results match selected rules', async () => {
    mockSummary({ ...summary, totalIssues: 0, issues: [] });
    const user = userEvent.setup();
    renderPage();

    await screen.findByText('اختر قاعدة جودة بيانات للبدء');
    await user.type(screen.getAllByPlaceholderText('عدد الأيام')[0], '10');
    await user.click(screen.getByRole('button', { name: 'تطبيق الفلاتر' }));

    expect(await screen.findByText('لا توجد ملاحظات مطابقة للفلاتر المحددة.')).toBeInTheDocument();
  });
});
