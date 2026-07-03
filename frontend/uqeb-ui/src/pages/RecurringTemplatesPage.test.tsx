import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { cleanup, render, screen, waitFor, fireEvent, within } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import RecurringTemplatesPage from './RecurringTemplatesPage';
import * as services from '../api/services';
import type { RecurringTemplateListItem, RecurringTemplateDetail, RecurringTemplateTransactionItem } from '../api/types';

vi.mock('../api/services', () => ({
  recurringTemplatesApi: {
    getAll: vi.fn(),
    getById: vi.fn(),
    getTransactions: vi.fn(),
    create: vi.fn(),
    update: vi.fn(),
    pause: vi.fn(),
    resume: vi.fn(),
    terminate: vi.fn(),
    generate: vi.fn(),
  },
  departmentsApi: { getAll: vi.fn() },
  categoriesApi: { getAll: vi.fn() },
  externalPartiesApi: { getAll: vi.fn() },
}));

const mockApi = vi.mocked(services.recurringTemplatesApi);

const activeTemplate: RecurringTemplateListItem = {
  id: 1,
  title: 'تقرير شهري من إدارة التشغيل',
  recurrenceType: 'Monthly',
  status: 'Active',
  startDate: '2026-01-01T00:00:00Z',
  nextPeriodKey: '2026-02',
  nextPeriodLabel: 'فبراير 2026',
  lastGeneratedPeriodKey: '2026-01',
  lastGeneratedPeriodLabel: 'يناير 2026',
  generatedTransactionsCount: 1,
  nextTransactionCreationMethod: 'Manual',
};

const pausedTemplate: RecurringTemplateListItem = {
  ...activeTemplate,
  id: 2,
  title: 'تقرير ربع سنوي من إدارة المالية',
  recurrenceType: 'Quarterly',
  status: 'Paused',
  generatedTransactionsCount: 0,
  lastGeneratedPeriodKey: undefined,
  lastGeneratedPeriodLabel: undefined,
};

const terminatedTemplate: RecurringTemplateListItem = {
  ...activeTemplate,
  id: 3,
  title: 'تقرير منتهٍ',
  status: 'Terminated',
};

const activeTemplateDetail: RecurringTemplateDetail = {
  ...activeTemplate,
  subjectTemplate: 'تقرير شهري من إدارة التشغيل',
  incomingSourceType: 'Internal',
  incomingFromDepartmentId: 10,
  incomingFromDepartmentName: 'التشغيل',
  categoryId: 1,
  categoryName: 'تقارير دورية',
  priority: 'Normal',
  responseType: 'Internal',
  requiresResponse: true,
  defaultRequiredAction: 'تزويدنا بالتقرير',
  dueDaysAfterPeriodEnd: 10,
  notes: undefined,
  departments: [{ departmentId: 10, departmentName: 'التشغيل' }],
  createdByName: 'مدير النظام',
  createdAt: '2026-01-01T00:00:00Z',
};

const sampleTransaction: RecurringTemplateTransactionItem = {
  transactionId: 100,
  internalTrackingNumber: 'UQEB-2026-00001',
  subject: 'تقرير شهري من إدارة التشغيل - يناير 2026',
  periodKey: '2026-01',
  periodLabel: 'يناير 2026',
  status: 'Assigned',
  incomingDate: '2026-02-01T00:00:00Z',
};

function renderPage(initialEntry = '/recurring-transaction-templates') {
  return render(
    <MemoryRouter initialEntries={[initialEntry]}>
      <RecurringTemplatesPage />
    </MemoryRouter>,
  );
}

beforeEach(() => {
  vi.clearAllMocks();
  mockApi.getAll.mockResolvedValue({ data: [activeTemplate, pausedTemplate, terminatedTemplate] } as never);
  mockApi.getById.mockResolvedValue({ data: activeTemplateDetail } as never);
  mockApi.getTransactions.mockResolvedValue({ data: [sampleTransaction] } as never);
  vi.mocked(services.departmentsApi.getAll).mockResolvedValue({ data: [] } as never);
  vi.mocked(services.categoriesApi.getAll).mockResolvedValue({ data: [] } as never);
  vi.mocked(services.externalPartiesApi.getAll).mockResolvedValue({ data: [] } as never);
});

afterEach(() => {
  cleanup();
});

describe('RecurringTemplatesPage', () => {
  it('renders the list of recurring templates with status labels', async () => {
    renderPage();
    await waitFor(() => {
      expect(screen.getByText('تقرير شهري من إدارة التشغيل')).toBeTruthy();
      expect(screen.getByText('نشط')).toBeTruthy();
      expect(screen.getByText('موقوف')).toBeTruthy();
      expect(screen.getByText('منتهٍ')).toBeTruthy();
    });
  });

  it('shows empty state when no templates exist', async () => {
    mockApi.getAll.mockResolvedValueOnce({ data: [] } as never);
    renderPage();
    await waitFor(() => {
      expect(screen.getByText('لا توجد قوالب التزامات دورية')).toBeTruthy();
    });
  });

  it('disables the generate button for a paused template', async () => {
    renderPage();
    await waitFor(() => screen.getByText('تقرير ربع سنوي من إدارة المالية'));
    const rows = screen.getAllByText('إنشاء معاملة للفترة');
    expect(rows[1]).toBeDisabled();
  });

  it('disables the generate button for a terminated template', async () => {
    renderPage();
    await waitFor(() => screen.getByText('تقرير منتهٍ'));
    const rows = screen.getAllByText('إنشاء معاملة للفترة');
    expect(rows[2]).toBeDisabled();
  });

  it('shows linked transactions when "عرض المعاملات" is clicked', async () => {
    renderPage();
    await waitFor(() => screen.getByText('تقرير شهري من إدارة التشغيل'));
    fireEvent.click(screen.getAllByText('عرض المعاملات')[0]);
    await waitFor(() => {
      expect(mockApi.getTransactions).toHaveBeenCalledWith(1);
      expect(screen.getByText('UQEB-2026-00001')).toBeTruthy();
    });
  });

  it('does not show one template\'s transactions under another template after switching', async () => {
    const templateOneTx = { ...sampleTransaction, transactionId: 100, internalTrackingNumber: 'UQEB-TEMPLATE-1' };
    const templateTwoTx = { ...sampleTransaction, transactionId: 200, internalTrackingNumber: 'UQEB-TEMPLATE-2' };
    mockApi.getTransactions.mockImplementation((id: number) =>
      Promise.resolve({ data: id === activeTemplate.id ? [templateOneTx] : [templateTwoTx] } as never));

    renderPage();
    await waitFor(() => screen.getByText('تقرير ربع سنوي من إدارة المالية'));
    const activeRow = screen.getByText('تقرير شهري من إدارة التشغيل').closest('tr')!;
    const pausedRow = screen.getByText('تقرير ربع سنوي من إدارة المالية').closest('tr')!;

    fireEvent.click(within(activeRow).getByText('عرض المعاملات'));
    await waitFor(() => expect(screen.getByText('UQEB-TEMPLATE-1')).toBeTruthy());

    fireEvent.click(within(pausedRow).getByText('عرض المعاملات'));
    await waitFor(() => expect(screen.getByText('UQEB-TEMPLATE-2')).toBeTruthy());
    expect(screen.queryByText('UQEB-TEMPLATE-1')).toBeNull();
  });

  it('does not re-fetch transactions when reopening a template already loaded', async () => {
    renderPage();
    await waitFor(() => screen.getByText('تقرير شهري من إدارة التشغيل'));
    const row = screen.getByText('تقرير شهري من إدارة التشغيل').closest('tr')!;
    const toggleButton = () => within(row).getByText(/عرض المعاملات|إخفاء المعاملات/);

    fireEvent.click(toggleButton());
    await waitFor(() => expect(screen.getByText('UQEB-2026-00001')).toBeTruthy());

    fireEvent.click(toggleButton());
    await waitFor(() => expect(screen.queryByText('UQEB-2026-00001')).toBeNull());

    fireEvent.click(toggleButton());
    await waitFor(() => expect(screen.getByText('UQEB-2026-00001')).toBeTruthy());

    expect(mockApi.getTransactions).toHaveBeenCalledTimes(1);
  });

  it('pauses an active template', async () => {
    mockApi.pause.mockResolvedValue({ data: { ...activeTemplateDetail, status: 'Paused' } } as never);
    renderPage();
    await waitFor(() => screen.getByText('تقرير شهري من إدارة التشغيل'));
    fireEvent.click(screen.getAllByText('إيقاف مؤقت')[0]);
    await waitFor(() => {
      expect(mockApi.pause).toHaveBeenCalledWith(1);
    });
  });

  it('resumes a paused template', async () => {
    mockApi.resume.mockResolvedValue({ data: { ...activeTemplateDetail, status: 'Active' } } as never);
    renderPage();
    await waitFor(() => screen.getByText('تقرير ربع سنوي من إدارة المالية'));
    fireEvent.click(screen.getByText('إعادة تفعيل'));
    await waitFor(() => {
      expect(mockApi.resume).toHaveBeenCalledWith(2);
    });
  });

  it('requires a reason before submitting template termination', async () => {
    renderPage();
    await waitFor(() => screen.getByText('تقرير شهري من إدارة التشغيل'));
    fireEvent.click(screen.getAllByText('إنهاء الاستمرار')[0]);

    await waitFor(() => screen.getByText('تأكيد الإنهاء'));
    fireEvent.click(screen.getByText('تأكيد الإنهاء'));

    await waitFor(() => {
      expect(screen.getByText('سبب الإنهاء مطلوب.')).toBeTruthy();
    });
    expect(mockApi.terminate).not.toHaveBeenCalled();
  });

  it('terminates a template once a reason is provided', async () => {
    mockApi.terminate.mockResolvedValue({ data: { ...activeTemplateDetail, status: 'Terminated' } } as never);
    renderPage();
    await waitFor(() => screen.getByText('تقرير شهري من إدارة التشغيل'));
    fireEvent.click(screen.getAllByText('إنهاء الاستمرار')[0]);

    await waitFor(() => screen.getByLabelText('سبب الإنهاء *'));
    fireEvent.change(screen.getByLabelText('سبب الإنهاء *'), { target: { value: 'توقف العمل بهذا التقرير' } });
    fireEvent.click(screen.getByText('تأكيد الإنهاء'));

    await waitFor(() => {
      expect(mockApi.terminate).toHaveBeenCalledWith(1, 'توقف العمل بهذا التقرير');
    });
  });

  it('opens the create template modal', async () => {
    renderPage();
    await waitFor(() => screen.getByText('إنشاء قالب دوري جديد'));
    fireEvent.click(screen.getByText('إنشاء قالب دوري جديد'));
    await waitFor(() => {
      expect(screen.getByText('إنشاء قالب التزام دوري')).toBeTruthy();
    });
  });

  it('highlights the template row when a highlight query param is present', async () => {
    renderPage('/recurring-transaction-templates?highlight=1');
    await waitFor(() => {
      const row = screen.getByText('تقرير شهري من إدارة التشغيل').closest('tr');
      expect(row?.className).toContain('row-highlighted');
    });
  });

  it('invalidates the expanded transactions cache for a template after generating a new period', async () => {
    mockApi.generate.mockResolvedValue({
      data: {
        transactionId: 999,
        internalTrackingNumber: 'UQEB-2026-00099',
        periodKey: '2026-02',
        periodLabel: 'فبراير 2026',
        dueDate: '2026-03-10',
      },
    } as never);

    renderPage();
    await waitFor(() => screen.getByText('تقرير شهري من إدارة التشغيل'));
    const row = screen.getByText('تقرير شهري من إدارة التشغيل').closest('tr')!;

    fireEvent.click(within(row).getByText('عرض المعاملات'));
    await waitFor(() => expect(mockApi.getTransactions).toHaveBeenCalledTimes(1));
    fireEvent.click(within(row).getByText('إخفاء المعاملات'));

    fireEvent.click(within(row).getByText('إنشاء معاملة للفترة'));
    await waitFor(() => screen.getByText('إنشاء المعاملة'));
    fireEvent.change(screen.getByLabelText('تاريخ الإحالة - اختيار من التقويم'), { target: { value: '2026-02-01' } });
    fireEvent.click(screen.getByText('إنشاء المعاملة'));

    await waitFor(() => expect(mockApi.generate).toHaveBeenCalledTimes(1));
    fireEvent.click(screen.getByText('إغلاق'));

    fireEvent.click(within(row).getByText('عرض المعاملات'));
    await waitFor(() => expect(mockApi.getTransactions).toHaveBeenCalledTimes(2));
  });

  it('opens the generate-period modal when a generate query param is present', async () => {
    renderPage('/recurring-transaction-templates?generate=1');
    await waitFor(() => {
      expect(mockApi.getById).toHaveBeenCalledWith(1);
      expect(screen.getByText(`إنشاء معاملة للفترة — ${activeTemplateDetail.title}`)).toBeTruthy();
    });
  });

  it('includes the next transaction creation method radio group in the template form', async () => {
    renderPage();
    await waitFor(() => screen.getByText('إنشاء قالب دوري جديد'));
    fireEvent.click(screen.getByText('إنشاء قالب دوري جديد'));
    await waitFor(() => screen.getByText('إنشاء قالب التزام دوري'));

    expect(screen.getByText('يدويًا من شاشة الالتزامات الدورية')).toBeTruthy();
    expect(screen.getByText('تلقائيًا عند إغلاق المعاملة الحالية')).toBeTruthy();

    const recurrenceSelect = screen.getByLabelText('نوع التكرار *') as HTMLSelectElement;
    expect(within(recurrenceSelect).getByText('نصف سنوي')).toBeTruthy();
    expect(within(recurrenceSelect).getByText('سنوي')).toBeTruthy();
  });

  it('shows a year+half picker when generating a period for a SemiAnnual template', async () => {
    mockApi.getById.mockResolvedValue({
      data: { ...activeTemplateDetail, recurrenceType: 'SemiAnnual', nextPeriodKey: '2026-H1', nextPeriodLabel: 'النصف الأول 2026' },
    } as never);

    renderPage();
    await waitFor(() => screen.getByText('تقرير شهري من إدارة التشغيل'));
    const row = screen.getByText('تقرير شهري من إدارة التشغيل').closest('tr')!;
    fireEvent.click(within(row).getByText('إنشاء معاملة للفترة'));

    await waitFor(() => screen.getByLabelText('النصف'));
    expect(screen.getByLabelText('النصف')).toBeTruthy();
    expect(screen.queryByLabelText('الشهر')).toBeNull();
  });

  it('shows a year picker when generating a period for an Annual template', async () => {
    mockApi.getById.mockResolvedValue({
      data: { ...activeTemplateDetail, recurrenceType: 'Annual', nextPeriodKey: '2026', nextPeriodLabel: 'سنة 2026' },
    } as never);

    renderPage();
    await waitFor(() => screen.getByText('تقرير شهري من إدارة التشغيل'));
    const row = screen.getByText('تقرير شهري من إدارة التشغيل').closest('tr')!;
    fireEvent.click(within(row).getByText('إنشاء معاملة للفترة'));

    await waitFor(() => screen.getByLabelText('السنة'));
    expect(screen.queryByLabelText('الشهر')).toBeNull();
    expect(screen.queryByLabelText('النصف')).toBeNull();
  });
});
