import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import TransactionForm from './TransactionForm';
import * as services from '../api/services';
import { formatHijriInputParts, gregorianToHijriParts } from '../utils/hijriDateInput';
import { todayLocalIso } from '../utils/localDate';

vi.mock('../api/services', () => ({
  transactionsApi: {
    getById: vi.fn(),
    create: vi.fn(),
    update: vi.fn(),
  },
  externalPartiesApi: { getAll: vi.fn() },
  departmentsApi: { getAll: vi.fn() },
  categoriesApi: { getAll: vi.fn() },
}));

const parties = [{ id: 10, name: 'جهة خارجية أ', type: 'Gov', isActive: true }];
const departments = [
  { id: 20, name: 'إدارة داخلية', code: 'D-01', isActive: true },
  { id: 21, name: 'إدارة المتابعة', code: 'D-02', isActive: true },
];
const categories = [{ id: 30, name: 'تصنيف عام', code: 'CAT-01', isActive: true }];

function mockReferenceData() {
  vi.mocked(services.externalPartiesApi.getAll).mockResolvedValue({ data: parties } as never);
  vi.mocked(services.departmentsApi.getAll).mockResolvedValue({ data: departments } as never);
  vi.mocked(services.categoriesApi.getAll).mockResolvedValue({ data: categories } as never);
}

function renderCreateForm() {
  return render(
    <MemoryRouter initialEntries={['/transactions/new']}>
      <Routes>
        <Route path="/transactions/new" element={<TransactionForm mode="create" />} />
      </Routes>
    </MemoryRouter>,
  );
}

function renderEditForm(id = '5') {
  return render(
    <MemoryRouter initialEntries={[`/transactions/${id}/edit`]}>
      <Routes>
        <Route path="/transactions/:id/edit" element={<TransactionForm mode="edit" />} />
      </Routes>
    </MemoryRouter>,
  );
}

async function waitForFormReady() {
  await waitFor(() => {
    expect(screen.queryByText('جاري التحميل...')).not.toBeInTheDocument();
  });
}

function getIncomingSection() {
  return getSectionByHeading('بيانات الوارد');
}

function getSectionByHeading(name: string) {
  const heading = screen.getAllByRole('heading', { name })[0];
  return heading.closest('section') as HTMLElement;
}

function getClassificationSection() {
  return getIncomingSection();
}

function getOutgoingSection() {
  return getRoutingSection();
}

function getRoutingSection() {
  return getSectionByHeading('بيانات التوجيه والإرسال الداخلي');
}

function getResponseSection() {
  return getIncomingSection();
}

async function openOutgoingDepartmentsDropdown(user: ReturnType<typeof userEvent.setup>) {
  const routingSection = getRoutingSection();
  await user.click(within(routingSection).getByRole('button', { name: /لم يتم اختيار أي إدارة|إدارة واحدة مختارة|إدارتان مختارتان/ }));
  return routingSection;
}

function getFieldInSection(section: HTMLElement, labelText: string) {
  const label = within(section).getByText(labelText, { selector: 'label' });
  const group = label.closest('.form-group');
  if (!group) throw new Error(`form-group not found for ${labelText}`);
  const control = group.querySelector('input, select, textarea');
  if (!control) throw new Error(`control not found for ${labelText}`);
  return control as HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement;
}

async function fillValidCreateForm(user: ReturnType<typeof userEvent.setup>) {
  const incomingSection = getIncomingSection();
  const responseSection = getResponseSection();
  await user.type(getFieldInSection(incomingSection, 'رقم المعاملة *'), 'IN-100');
  await user.type(getFieldInSection(incomingSection, 'تاريخ المعاملة *'), hijriInputForGregorian('2026-06-01'));
  await user.type(getFieldInSection(incomingSection, 'الموضوع *'), 'اختبار');
  await user.click(screen.getByRole('combobox', { name: /الجهة الوارد منها/ }));
  await user.click(screen.getByRole('option', { name: /جهة خارجية أ/ }));
  await user.click(screen.getByRole('combobox', { name: /التصنيف/ }));
  await user.click(screen.getByRole('option', { name: /تصنيف عام/ }));
  await user.type(getFieldInSection(responseSection, 'عدد الأيام للرد *'), '7');
}

function validationError(errors: Record<string, string | string[]>) {
  return {
    isAxiosError: true,
    message: 'Request failed with status code 400',
    response: {
      status: 400,
      data: { errors },
      headers: {},
    },
  };
}

function addDaysIso(value: string, days: number) {
  const [year, month, day] = value.split('-').map(Number);
  const date = new Date(year, month - 1, day, 12, 0, 0);
  date.setDate(date.getDate() + days);
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, '0')}-${String(date.getDate()).padStart(2, '0')}`;
}

function hijriInputForGregorian(value: string) {
  const parts = gregorianToHijriParts(value);
  if (!parts) throw new Error(`Cannot convert ${value} to Hijri`);
  return formatHijriInputParts(parts);
}

afterEach(() => {
  cleanup();
});

describe('TransactionForm bootstrap', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockReferenceData();
  });

  it('loads reference data successfully in create mode', async () => {
    renderCreateForm();
    await waitForFormReady();

    expect(services.externalPartiesApi.getAll).toHaveBeenCalledWith(true);
    expect(services.departmentsApi.getAll).toHaveBeenCalledWith(true);
    expect(services.categoriesApi.getAll).toHaveBeenCalledWith(true);
    expect(screen.queryByText(/تعذر تحميل/)).not.toBeInTheDocument();
  });

  it('shows error when reference data bootstrap fails', async () => {
    vi.mocked(services.externalPartiesApi.getAll).mockRejectedValue(new Error('network'));
    vi.mocked(services.departmentsApi.getAll).mockResolvedValue({ data: [] } as never);
    vi.mocked(services.categoriesApi.getAll).mockResolvedValue({ data: [] } as never);

    renderCreateForm();

    await waitFor(() => {
      expect(screen.getByText(/تعذر تحميل: الجهات الخارجية/)).toBeInTheDocument();
    });
  });
});

describe('TransactionForm incoming layout', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockReferenceData();
  });

  it('TransactionForm_RendersGroupedSections', async () => {
    renderCreateForm();
    await waitForFormReady();

    expect(screen.getByRole('heading', { name: 'بيانات الوارد' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'بيانات التوجيه والإرسال الداخلي' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'ملاحظات' })).toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'التصنيف والأولوية' })).not.toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'بيانات الإفادة' })).not.toBeInTheDocument();
    expect(screen.queryByRole('heading', { name: 'بيانات الصادر' })).not.toBeInTheDocument();
  });

  it('TransactionForm_HasSingleMainHeading', async () => {
    renderCreateForm();
    await waitForFormReady();

    expect(screen.getAllByRole('heading', { name: 'إضافة معاملة' })).toHaveLength(1);
    expect(screen.queryByText('إدخال بيانات معاملة جديدة')).not.toBeInTheDocument();
  });

  it('shows category inside classification card', async () => {
    renderCreateForm();
    await waitForFormReady();

    const classificationSection = getClassificationSection();
    expect(within(classificationSection).getByRole('combobox', { name: /التصنيف/ })).toBeInTheDocument();
  });

  it('shows response fields inside response card', async () => {
    renderCreateForm();
    await waitForFormReady();

    const responseSection = getResponseSection();
    expect(getFieldInSection(responseSection, 'نوع الإفادة *')).toBeInTheDocument();
    expect(getFieldInSection(responseSection, 'عدد الأيام للرد *')).toBeInTheDocument();
  });

  it('does not duplicate category in outgoing card', async () => {
    renderCreateForm();
    await waitForFormReady();

    const outgoingSection = getOutgoingSection();
    expect(within(outgoingSection).queryByRole('combobox', { name: /التصنيف/ })).not.toBeInTheDocument();
  });

  it('loads saved category value in edit mode', async () => {
    vi.mocked(services.transactionsApi.getById).mockResolvedValue({
      data: {
        incomingNumber: 'IN-1',
        incomingDate: '2026-06-01T00:00:00',
        subject: 'موضوع',
        incomingSourceType: 'External',
        incomingFromPartyId: 10,
        incomingFromDepartmentId: null,
        outgoingNumber: '',
        outgoingDate: null,
        outgoingDepartments: [],
        responseType: 'External',
        responseDueDays: 5,
        priority: 'Normal',
        categoryId: 30,
        notes: '',
      },
    } as never);

    renderEditForm();
    await waitForFormReady();

    expect(screen.getByRole('combobox', { name: /التصنيف/ })).toHaveValue('تصنيف عام');
  });
});

describe('TransactionForm searchable selects', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockReferenceData();
  });

  it('TransactionForm_ExternalPartyType_ShowsExternalDropdown', async () => {
    const user = userEvent.setup();
    renderCreateForm();
    await waitForFormReady();

    await user.click(screen.getByRole('combobox', { name: /الجهة الوارد منها/ }));
    await user.click(screen.getByRole('option', { name: /جهة خارجية أ/ }));

    expect(screen.getByRole('combobox', { name: /الجهة الوارد منها/ })).toHaveValue('جهة خارجية أ');
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
  });

  it('TransactionForm_InternalPartyType_ShowsInternalDropdown', async () => {
    const user = userEvent.setup();
    renderCreateForm();
    await waitForFormReady();

    await user.click(screen.getByRole('radio', { name: 'داخلية' }));
    await user.click(screen.getByRole('combobox', { name: /الجهة الوارد منها/ }));
    await user.click(screen.getByRole('option', { name: /إدارة داخلية/ }));

    expect(screen.getByRole('combobox', { name: /الجهة الوارد منها/ })).toHaveValue('إدارة داخلية');
  });

  it('selects category on mouse click', async () => {
    const user = userEvent.setup();
    renderCreateForm();
    await waitForFormReady();

    await user.click(screen.getByRole('combobox', { name: /التصنيف/ }));
    await user.click(screen.getByRole('option', { name: /تصنيف عام/ }));

    expect(screen.getByRole('combobox', { name: /التصنيف/ })).toHaveValue('تصنيف عام');
  });

  it('selects priority via native select on click', async () => {
    const user = userEvent.setup();
    renderCreateForm();
    await waitForFormReady();

    const prioritySelect = getFieldInSection(getClassificationSection(), 'الأولوية *');
    await user.selectOptions(prioritySelect, 'Urgent');
    expect(prioritySelect).toHaveValue('Urgent');
  });

  it('includes selected values in create payload', async () => {
    const user = userEvent.setup();
    vi.mocked(services.transactionsApi.create).mockResolvedValue({ data: { id: 99 } } as never);

    renderCreateForm();
    await waitForFormReady();

    const incomingSection = getIncomingSection();
    const classificationSection = getClassificationSection();
    const responseSection = getResponseSection();
    await user.type(getFieldInSection(incomingSection, 'رقم المعاملة *'), 'IN-100');
    await user.type(getFieldInSection(incomingSection, 'تاريخ المعاملة *'), hijriInputForGregorian('2026-06-01'));
    await user.type(getFieldInSection(incomingSection, 'الموضوع *'), 'اختبار');
    await user.click(screen.getByRole('combobox', { name: /الجهة الوارد منها/ }));
    await user.click(screen.getByRole('option', { name: /جهة خارجية أ/ }));
    await user.click(screen.getByRole('combobox', { name: /التصنيف/ }));
    await user.click(screen.getByRole('option', { name: /تصنيف عام/ }));
    await user.selectOptions(getFieldInSection(classificationSection, 'الأولوية *'), 'Urgent');
    await user.selectOptions(getFieldInSection(responseSection, 'نوع الإفادة *'), 'Internal');
    await user.type(getFieldInSection(responseSection, 'عدد الأيام للرد *'), '7');
    await user.click(screen.getByRole('button', { name: 'حفظ' }));

    await waitFor(() => {
      expect(services.transactionsApi.create).toHaveBeenCalled();
    });

    const payload = vi.mocked(services.transactionsApi.create).mock.calls[0][0];
    expect(payload.incomingFromPartyId).toBe(10);
    expect(payload.categoryId).toBe(30);
    expect(payload.priority).toBe('Urgent');
    expect(payload.responseType).toBe('Internal');
    expect(payload.responseDueDays).toBe(7);
  });

  it('works in edit mode after loading transaction', async () => {
    vi.mocked(services.transactionsApi.getById).mockResolvedValue({
      data: {
        incomingNumber: 'IN-EDIT',
        incomingDate: '2026-06-01T00:00:00',
        subject: 'تعديل',
        incomingSourceType: 'Internal',
        incomingFromPartyId: null,
        incomingFromDepartmentId: 20,
        outgoingNumber: '',
        outgoingDate: null,
        outgoingDepartments: [],
        responseType: 'Both',
        responseDueDays: 3,
        priority: 'VeryUrgent',
        categoryId: 30,
        notes: 'ملاحظة',
      },
    } as never);
    vi.mocked(services.transactionsApi.update).mockResolvedValue({} as never);

    const user = userEvent.setup();
    renderEditForm();
    await waitForFormReady();

    const incomingSection = getIncomingSection();
    expect(getFieldInSection(incomingSection, 'رقم المعاملة *')).toHaveValue('IN-EDIT');
    expect(screen.getByRole('combobox', { name: /الجهة الوارد منها/ })).toHaveValue('إدارة داخلية');
    expect(screen.getByRole('combobox', { name: /التصنيف/ })).toHaveValue('تصنيف عام');
    expect(getFieldInSection(getResponseSection(), 'نوع الإفادة *')).toHaveValue('Both');

    await user.click(screen.getByRole('button', { name: 'حفظ' }));

    await waitFor(() => {
      expect(services.transactionsApi.update).toHaveBeenCalled();
    });
  });

  it('EditTransaction_ResponseTypeNone_PreservesStoredValue', async () => {
    vi.mocked(services.transactionsApi.getById).mockResolvedValue({
      data: {
        incomingNumber: 'IN-NONE',
        incomingDate: '2026-06-01T00:00:00',
        subject: 'معاملة قديمة',
        incomingSourceType: 'External',
        incomingFromPartyId: 10,
        incomingFromDepartmentId: null,
        outgoingNumber: '',
        outgoingDate: null,
        outgoingDepartments: [],
        responseType: 'None',
        responseDueDays: null,
        priority: 'Normal',
        categoryId: 30,
        notes: '',
      },
    } as never);

    renderEditForm();
    await waitForFormReady();

    expect(getFieldInSection(getResponseSection(), 'نوع الإفادة *')).toHaveValue('None');
  });

  it('EditTransaction_ResponseTypeNone_DoesNotRequireDueDaysUnlessResponseIsRequired', async () => {
    const user = userEvent.setup();
    vi.mocked(services.transactionsApi.getById).mockResolvedValue({
      data: {
        incomingNumber: 'IN-NONE',
        incomingDate: '2026-06-01T00:00:00',
        subject: 'معاملة قديمة',
        incomingSourceType: 'External',
        incomingFromPartyId: 10,
        incomingFromDepartmentId: null,
        outgoingNumber: '',
        outgoingDate: null,
        outgoingDepartments: [],
        responseType: 'None',
        responseDueDays: null,
        priority: 'Normal',
        categoryId: 30,
        notes: '',
      },
    } as never);
    vi.mocked(services.transactionsApi.update).mockResolvedValue({} as never);

    renderEditForm();
    await waitForFormReady();

    await user.click(screen.getByRole('button', { name: 'حفظ' }));

    await waitFor(() => {
      expect(services.transactionsApi.update).toHaveBeenCalled();
    });
    const payload = vi.mocked(services.transactionsApi.update).mock.calls[0][1];
    expect(payload.responseType).toBe('None');
    expect(payload.responseDueDays).toBeNull();
    expect(payload.requiresResponse).toBe(false);
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });

  it('TransactionForm_UsesResponsiveFieldLayout', async () => {
    renderCreateForm();
    await waitForFormReady();

    const grid = getIncomingSection().querySelector('.transaction-form-grid--incoming');
    const incomingNumberGroup = getFieldInSection(getIncomingSection(), 'رقم المعاملة *').closest('.form-group');
    const subjectGroup = getIncomingSection().querySelector('.transaction-subject-field');

    expect(grid).toBeTruthy();
    expect(grid?.classList.contains('transaction-form-grid')).toBe(true);
    expect(incomingNumberGroup).toHaveClass('transaction-form-field--compact');
    expect(subjectGroup).toHaveClass('transaction-form-field--wide');
  });

  it('associates transaction number label with its input', async () => {
    renderCreateForm();
    await waitForFormReady();

    const transactionNumber = screen.getByLabelText('رقم المعاملة *');
    expect(transactionNumber).toHaveAttribute('id', 'incoming-number');
  });

  it('renders subject field across full grid width', async () => {
    renderCreateForm();
    await waitForFormReady();

    const subjectGroup = getIncomingSection().querySelector('.transaction-subject-field');
    expect(subjectGroup).toBeTruthy();
    expect(subjectGroup?.querySelector('input')).toBeTruthy();
  });

  it('does not render hidden source wrapper when switching to internal source', async () => {
    const user = userEvent.setup();
    renderCreateForm();
    await waitForFormReady();

    const incomingSection = getIncomingSection();
    expect(within(incomingSection).getByRole('combobox', { name: /الجهة الوارد منها/ })).toBeInTheDocument();

    await user.click(screen.getByRole('radio', { name: 'داخلية' }));

    expect(within(incomingSection).queryAllByRole('combobox', { name: /الجهة الوارد منها/ })).toHaveLength(1);
    expect(within(incomingSection).queryByText(/جهة خارجية أ/)).not.toBeInTheDocument();
  });

  it('does not fire duplicate onChange when selecting with mouse', async () => {
    const user = userEvent.setup();
    renderCreateForm();
    await waitForFormReady();

    await user.click(screen.getByRole('combobox', { name: /التصنيف/ }));
    await user.click(screen.getByRole('option', { name: /تصنيف عام/ }));

    expect(screen.getByRole('combobox', { name: /التصنيف/ })).toHaveValue('تصنيف عام');
  });

  it('preserves selected value after combobox blur', async () => {
    const user = userEvent.setup();
    renderCreateForm();
    await waitForFormReady();

    await user.click(screen.getByRole('combobox', { name: /التصنيف/ }));
    await user.click(screen.getByRole('option', { name: /تصنيف عام/ }));
    await user.click(screen.getByRole('button', { name: 'حفظ' }));

    expect(screen.getByRole('combobox', { name: /التصنيف/ })).toHaveValue('تصنيف عام');
  });

  it('TransactionForm_InternalDropdown_AllowsMultipleSelection', async () => {
    const user = userEvent.setup();
    renderCreateForm();
    await waitForFormReady();

    const routingSection = await openOutgoingDepartmentsDropdown(user);
    await user.click(within(routingSection).getByLabelText('إدارة داخلية'));
    await user.click(within(routingSection).getByLabelText('إدارة المتابعة'));

    expect(within(routingSection).getByText('إدارتان مختارتان')).toBeInTheDocument();
    expect(within(routingSection).getByRole('button', { name: 'إزالة إدارة داخلية' })).toBeInTheDocument();
    expect(within(routingSection).getByRole('button', { name: 'إزالة إدارة المتابعة' })).toBeInTheDocument();
  });

  it('TransactionForm_InternalDropdown_PreventsDuplicateSelection', async () => {
    const user = userEvent.setup();
    renderCreateForm();
    await waitForFormReady();

    const routingSection = await openOutgoingDepartmentsDropdown(user);
    await user.click(within(routingSection).getByLabelText('إدارة داخلية'));

    expect(within(routingSection).getAllByRole('button', { name: 'إزالة إدارة داخلية' })).toHaveLength(1);
    expect(within(routingSection).getByText('إدارة واحدة مختارة')).toBeInTheDocument();
  });

  it('TransactionForm_InternalDropdown_RemovesSelectedDepartment', async () => {
    const user = userEvent.setup();
    renderCreateForm();
    await waitForFormReady();

    const routingSection = await openOutgoingDepartmentsDropdown(user);
    await user.click(within(routingSection).getByLabelText('إدارة داخلية'));
    await user.click(within(routingSection).getByRole('button', { name: 'إزالة إدارة داخلية' }));

    expect(within(routingSection).queryByRole('button', { name: 'إزالة إدارة داخلية' })).not.toBeInTheDocument();
    expect(within(routingSection).getByText('لم يتم اختيار أي إدارة')).toBeInTheDocument();
  });

  it('TransactionForm_SubmitsSelectedInternalDepartmentIds', async () => {
    const user = userEvent.setup();
    vi.mocked(services.transactionsApi.create).mockResolvedValue({ data: { id: 99 } } as never);

    renderCreateForm();
    await waitForFormReady();
    await fillValidCreateForm(user);

    const outgoingSection = getOutgoingSection();
    await user.type(getFieldInSection(outgoingSection, 'رقم الصادر'), 'OUT-100');
    await user.type(getFieldInSection(outgoingSection, 'تاريخ الصادر'), hijriInputForGregorian(todayLocalIso()));
    const routingSection = await openOutgoingDepartmentsDropdown(user);
    await user.click(within(routingSection).getByLabelText('إدارة داخلية'));
    await user.click(within(routingSection).getByLabelText('إدارة المتابعة'));
    await user.click(screen.getByRole('button', { name: 'حفظ' }));

    await waitFor(() => expect(services.transactionsApi.create).toHaveBeenCalled());
    const payload = vi.mocked(services.transactionsApi.create).mock.calls[0][0];
    expect(payload.outgoingDepartmentIds).toEqual([20, 21]);
  });

  it('converts Hijri incoming date to Gregorian ISO in create payload', async () => {
    const user = userEvent.setup();
    vi.mocked(services.transactionsApi.create).mockResolvedValue({ data: { id: 99 } } as never);

    renderCreateForm();
    await waitForFormReady();
    await fillValidCreateForm(user);

    const incomingDate = getFieldInSection(getIncomingSection(), 'تاريخ المعاملة *');
    await user.clear(incomingDate);
    await user.type(incomingDate, '16/01/1448');
    await user.click(screen.getByRole('button', { name: 'حفظ' }));

    await waitFor(() => expect(services.transactionsApi.create).toHaveBeenCalled());
    const payload = vi.mocked(services.transactionsApi.create).mock.calls[0][0];
    expect(payload.incomingDate).toBe('2026-07-01T00:00:00');
  });
});

describe('TransactionForm validation feedback', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockReferenceData();
  });

  it('CreateTransaction_MissingRequiredFields_ShowsFieldErrors', async () => {
    const user = userEvent.setup();
    renderCreateForm();
    await waitForFormReady();

    await user.click(screen.getByRole('button', { name: 'حفظ' }));

    const alert = screen.getByRole('alert');
    expect(alert).toHaveTextContent('يرجى تصحيح الحقول التالية:');
    expect(alert).toHaveTextContent('رقم المعاملة مطلوب.');
    expect(alert).toHaveTextContent('تاريخ المعاملة مطلوب.');
    expect(alert).toHaveTextContent('الموضوع مطلوب.');
    expect(alert).toHaveTextContent('الجهة الخارجية مطلوبة.');
    expect(alert).toHaveTextContent('التصنيف مطلوب.');
    expect(alert).toHaveTextContent('عدد أيام الرد مطلوب عند طلب إفادة.');
    expect(services.transactionsApi.create).not.toHaveBeenCalled();
  });

  it('TransactionForm_MissingInternalDepartment_ShowsValidationError', async () => {
    const user = userEvent.setup();
    renderCreateForm();
    await waitForFormReady();

    await user.click(screen.getByRole('radio', { name: 'داخلية' }));
    await user.click(screen.getByRole('button', { name: 'حفظ' }));

    expect(screen.getByRole('alert')).toHaveTextContent('الجهة الداخلية مطلوبة.');
    expect(document.querySelector('[data-field-name="incomingFromDepartmentId"]')).toHaveAttribute('aria-invalid', 'true');
  });

  it('CreateTransaction_MissingIncomingNumber_HighlightsIncomingNumber', async () => {
    const user = userEvent.setup();
    renderCreateForm();
    await waitForFormReady();

    await user.click(screen.getByRole('button', { name: 'حفظ' }));

    const incomingNumber = getFieldInSection(getIncomingSection(), 'رقم المعاملة *');
    expect(incomingNumber).toHaveAttribute('aria-invalid', 'true');
    expect(incomingNumber.closest('.form-group')).toHaveClass('has-error');
    await waitFor(() => expect(incomingNumber).toHaveFocus());
  });

  it('CreateTransaction_MissingIncomingDate_HighlightsIncomingDate', async () => {
    const user = userEvent.setup();
    renderCreateForm();
    await waitForFormReady();

    const incomingSection = getIncomingSection();
    const incomingNumber = getFieldInSection(incomingSection, 'رقم المعاملة *');
    const incomingDate = getFieldInSection(incomingSection, 'تاريخ المعاملة *');
    await user.type(incomingNumber, 'IN-101');
    await user.clear(incomingDate);
    await user.click(screen.getByRole('button', { name: 'حفظ' }));

    expect(incomingDate).toHaveAttribute('aria-invalid', 'true');
    expect(screen.getByRole('alert')).toHaveTextContent('تاريخ المعاملة مطلوب.');
  });

  it('CreateTransaction_IncomingDate_StartsEmpty', async () => {
    renderCreateForm();
    await waitForFormReady();

    expect(getFieldInSection(getIncomingSection(), 'تاريخ المعاملة *')).toHaveValue('');
  });

  it('CreateTransaction_MissingSubject_HighlightsSubject', async () => {
    const user = userEvent.setup();
    renderCreateForm();
    await waitForFormReady();

    await user.click(screen.getByRole('button', { name: 'حفظ' }));

    const subject = getFieldInSection(getIncomingSection(), 'الموضوع *');
    expect(subject).toHaveAttribute('aria-invalid', 'true');
    expect(subject.closest('.form-group')).toHaveClass('has-error');
  });

  it('CreateTransaction_BackendValidationErrors_AreMappedToFields', async () => {
    const user = userEvent.setup();
    vi.mocked(services.transactionsApi.create).mockRejectedValue(validationError({
      IncomingNumber: 'رقم المعاملة مطلوب.',
      ResponseDueDays: ['عدد أيام الرد مطلوب عند طلب إفادة.'],
    }) as never);
    renderCreateForm();
    await waitForFormReady();
    await fillValidCreateForm(user);

    await user.click(screen.getByRole('button', { name: 'حفظ' }));

    const incomingNumber = getFieldInSection(getIncomingSection(), 'رقم المعاملة *');
    const responseDueDays = getFieldInSection(getResponseSection(), 'عدد الأيام للرد *');
    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('رقم المعاملة مطلوب.'));
    expect(incomingNumber).toHaveAttribute('aria-invalid', 'true');
    expect(responseDueDays).toHaveAttribute('aria-invalid', 'true');
  });

  it('CreateTransaction_SaveFailure_DoesNotClearForm', async () => {
    const user = userEvent.setup();
    vi.mocked(services.transactionsApi.create).mockRejectedValue(validationError({
      Subject: 'الموضوع مطلوب.',
    }) as never);
    renderCreateForm();
    await waitForFormReady();
    await fillValidCreateForm(user);

    await user.click(screen.getByRole('button', { name: 'حفظ' }));

    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('الموضوع مطلوب.'));
    expect(getFieldInSection(getIncomingSection(), 'رقم المعاملة *')).toHaveValue('IN-100');
    expect(screen.getByRole('combobox', { name: /الجهة الوارد منها/ })).toHaveValue('جهة خارجية أ');
    expect(screen.getByRole('combobox', { name: /التصنيف/ })).toHaveValue('تصنيف عام');
  });

  it('CreateTransaction_MissingOutgoingDepartmentIds_FocusesOrMarksMultiSelect', async () => {
    const user = userEvent.setup();
    renderCreateForm();
    await waitForFormReady();
    await fillValidCreateForm(user);

    const outgoingSection = getOutgoingSection();
    await user.type(getFieldInSection(outgoingSection, 'رقم الصادر'), 'OUT-100');
    await user.type(getFieldInSection(outgoingSection, 'تاريخ الصادر'), hijriInputForGregorian(todayLocalIso()));
    await user.click(screen.getByRole('button', { name: 'حفظ' }));

    const outgoingDepartments = document.querySelector<HTMLElement>('[data-field-name="outgoingDepartmentIds"]');
    expect(outgoingDepartments).not.toBeNull();
    await waitFor(() => expect(outgoingDepartments).toHaveFocus());
    expect(screen.getByRole('alert')).toHaveTextContent('اختر جهة داخلية واحدة على الأقل.');
  });

  it('CreateTransaction_MissingOutgoingDepartmentIds_SetsInvalidClassAndDescribedBy', async () => {
    const user = userEvent.setup();
    renderCreateForm();
    await waitForFormReady();
    await fillValidCreateForm(user);

    const outgoingSection = getOutgoingSection();
    await user.type(getFieldInSection(outgoingSection, 'رقم الصادر'), 'OUT-100');
    await user.type(getFieldInSection(outgoingSection, 'تاريخ الصادر'), hijriInputForGregorian(todayLocalIso()));
    await user.click(screen.getByRole('button', { name: 'حفظ' }));

    const outgoingDepartments = document.querySelector<HTMLElement>('[data-field-name="outgoingDepartmentIds"]');
    expect(outgoingDepartments).not.toBeNull();
    await waitFor(() => {
      expect(outgoingDepartments).toHaveClass('is-invalid');
      expect(outgoingDepartments).not.toHaveAttribute('aria-invalid');
      expect(outgoingDepartments).toHaveAttribute('aria-describedby', 'outgoingDepartmentIds-error');
    });
    expect(document.querySelectorAll('#outgoingDepartmentIds-error')).toHaveLength(1);
  });

  it('CreateTransaction_FutureIncomingDate_ShowsValidationError', async () => {
    const user = userEvent.setup();
    renderCreateForm();
    await waitForFormReady();
    await fillValidCreateForm(user);

    const incomingDate = getFieldInSection(getIncomingSection(), 'تاريخ المعاملة *');
    await user.clear(incomingDate);
    await user.type(incomingDate, hijriInputForGregorian(addDaysIso(todayLocalIso(), 1)));
    await user.click(screen.getByRole('button', { name: 'حفظ' }));

    expect(screen.getByRole('alert')).toHaveTextContent('تاريخ المعاملة لا يمكن أن يكون بعد تاريخ اليوم.');
    expect(services.transactionsApi.create).not.toHaveBeenCalled();
  });

  it('CreateTransaction_OutgoingNumberWithoutDate_ShowsSpecificValidationError', async () => {
    const user = userEvent.setup();
    renderCreateForm();
    await waitForFormReady();
    await fillValidCreateForm(user);

    await user.type(getFieldInSection(getOutgoingSection(), 'رقم الصادر'), 'OUT-100');
    await user.click(screen.getByRole('button', { name: 'حفظ' }));

    expect(screen.getByRole('alert')).toHaveTextContent('تاريخ الصادر مطلوب عند إدخال رقم الصادر.');
    expect(services.transactionsApi.create).not.toHaveBeenCalled();
  });

  it('CreateTransaction_FutureOutgoingDate_ShowsValidationError', async () => {
    const user = userEvent.setup();
    renderCreateForm();
    await waitForFormReady();
    await fillValidCreateForm(user);

    const outgoingSection = getOutgoingSection();
    await user.type(getFieldInSection(outgoingSection, 'رقم الصادر'), 'OUT-100');
    await user.type(getFieldInSection(outgoingSection, 'تاريخ الصادر'), hijriInputForGregorian(addDaysIso(todayLocalIso(), 1)));
    await user.click(screen.getByRole('button', { name: 'حفظ' }));

    expect(screen.getByRole('alert')).toHaveTextContent('تاريخ الصادر لا يمكن أن يكون بعد تاريخ اليوم.');
    expect(services.transactionsApi.create).not.toHaveBeenCalled();
  });

  it('CreateTransaction_OutgoingDateBeforeTransactionDate_ShowsValidationError', async () => {
    const user = userEvent.setup();
    renderCreateForm();
    await waitForFormReady();
    await fillValidCreateForm(user);

    const incomingSection = getIncomingSection();
    const outgoingSection = getOutgoingSection();
    await user.clear(getFieldInSection(incomingSection, 'تاريخ المعاملة *'));
    await user.type(getFieldInSection(incomingSection, 'تاريخ المعاملة *'), '16/01/1448');
    await user.type(getFieldInSection(outgoingSection, 'رقم الصادر'), 'OUT-100');
    await user.type(getFieldInSection(outgoingSection, 'تاريخ الصادر'), '15/01/1448');
    await user.click(screen.getByRole('button', { name: 'حفظ' }));

    expect(screen.getByRole('alert')).toHaveTextContent('تاريخ الصادر لا يمكن أن يكون قبل تاريخ المعاملة.');
    expect(services.transactionsApi.create).not.toHaveBeenCalled();
  });

  it('EditTransaction_MissingRequiredFields_ShowsFieldErrors', async () => {
    const user = userEvent.setup();
    vi.mocked(services.transactionsApi.getById).mockResolvedValue({
      data: {
        incomingNumber: '',
        incomingDate: '2026-06-01T00:00:00',
        subject: '',
        incomingSourceType: 'External',
        incomingFromPartyId: null,
        incomingFromDepartmentId: null,
        outgoingNumber: '',
        outgoingDate: null,
        outgoingDepartments: [],
        responseType: 'External',
        responseDueDays: null,
        priority: 'Normal',
        categoryId: null,
        notes: '',
      },
    } as never);
    renderEditForm();
    await waitForFormReady();

    await user.click(screen.getByRole('button', { name: 'حفظ' }));

    expect(screen.getByRole('alert')).toHaveTextContent('رقم المعاملة مطلوب.');
    expect(screen.getByRole('alert')).toHaveTextContent('الموضوع مطلوب.');
    expect(services.transactionsApi.update).not.toHaveBeenCalled();
  });
});
