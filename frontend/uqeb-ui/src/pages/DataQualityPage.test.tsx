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
    renderPage();

    expect(await screen.findByText('جودة البيانات')).toBeInTheDocument();
    expect(screen.getByText('إجمالي الملاحظات')).toBeInTheDocument();
    expect(screen.getByText('معاملات متأثرة')).toBeInTheDocument();
    expect(screen.getByText('مدة التأخر تتجاوز الحد المحدد')).toBeInTheDocument();
    expect(screen.getByText('تاريخ الإحالة أكبر من تاريخ الوارد')).toBeInTheDocument();
    expect(screen.getByText('فترة الرد أقل من الحد المحدد')).toBeInTheDocument();
  });

  it('sends selected rule filters to the API', async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findAllByText('مدة التأخر تتجاوز الحد المحدد');

    const dayInputs = screen.getAllByPlaceholderText('عدد الأيام');
    await user.type(dayInputs[0], '10');
    await user.click(screen.getByLabelText('عرض المعاملات التي تاريخ الإحالة فيها أكبر من تاريخ الوارد'));
    await user.type(dayInputs[1], '5');
    await user.click(screen.getByRole('button', { name: 'تطبيق الفلاتر' }));

    await waitFor(() => expect(services.dataQualityApi.getSummary).toHaveBeenLastCalledWith(
      expect.objectContaining({
        overdueMoreThanDays: 10,
        includeReferralDateAfterIncomingDate: true,
        responsePeriodLessThanDays: 5,
      }),
    ));
  });

  it('maps review filter values to API parameters', async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findAllByText('مدة التأخر تتجاوز الحد المحدد');

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
    await screen.findAllByText('مدة التأخر تتجاوز الحد المحدد');

    const dayInputs = screen.getAllByPlaceholderText('عدد الأيام');
    await user.type(dayInputs[0], '10');
    await user.click(screen.getByRole('button', { name: 'تطبيق الفلاتر' }));
    await waitFor(() => expect(services.dataQualityApi.getSummary).toHaveBeenLastCalledWith(
      expect.objectContaining({ overdueMoreThanDays: 10 }),
    ));

    await user.clear(dayInputs[0]);
    await user.type(dayInputs[0], '20');

    await user.click(screen.getAllByRole('button', { name: 'تمت المراجعة' })[0]);
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
    await screen.findAllByText('مدة التأخر تتجاوز الحد المحدد');

    await user.click(screen.getAllByRole('button', { name: 'تمت المراجعة' })[0]);

    expect(await screen.findByText('تعذر تعليم الملاحظة كمراجعة.')).toBeInTheDocument();
    expect(screen.queryByText('تمت مراجعة هذه الملاحظة، ولن تظهر في النتائج الافتراضية القادمة.')).not.toBeInTheDocument();
    expect(services.dataQualityApi.getSummary).toHaveBeenCalledTimes(1);
  });

  it('shows an error without success feedback when unmarking an issue fails', async () => {
    vi.mocked(services.dataQualityApi.unmarkReviewed).mockRejectedValueOnce(new Error('failed'));
    const user = userEvent.setup();
    renderPage();
    await screen.findAllByText('مدة التأخر تتجاوز الحد المحدد');

    await user.click(screen.getByRole('button', { name: 'إزالة المراجعة' }));

    expect(await screen.findByText('تعذر إزالة علامة المراجعة.')).toBeInTheDocument();
    expect(screen.queryByText('تمت إزالة علامة المراجعة، وستعود الملاحظة للظهور إذا بقيت القاعدة منطبقة.')).not.toBeInTheDocument();
    expect(services.dataQualityApi.getSummary).toHaveBeenCalledTimes(1);
  });

  it('opens the existing transaction details page', async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findAllByText('مدة التأخر تتجاوز الحد المحدد');

    await user.click(screen.getAllByRole('button', { name: 'فتح المعاملة' })[0]);

    expect(navigate).toHaveBeenCalledWith('/transactions/1');
  });

  it('shows an empty state when no results match filters', async () => {
    mockSummary({ ...summary, totalIssues: 0, issues: [] });

    renderPage();

    expect(await screen.findByText('لا توجد ملاحظات مطابقة للفلاتر المحددة.')).toBeInTheDocument();
  });
});
