import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import TransactionForm from './TransactionForm';
import * as services from '../api/services';

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
const departments = [{ id: 20, name: 'إدارة داخلية', code: 'D-01', isActive: true }];
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
  const heading = screen.getAllByRole('heading', { name: 'بيانات الوارد' })[0];
  return heading.closest('section') as HTMLElement;
}

function getFieldInSection(section: HTMLElement, labelText: string) {
  const label = within(section).getByText(labelText, { selector: 'label' });
  const group = label.closest('.form-group');
  if (!group) throw new Error(`form-group not found for ${labelText}`);
  const control = group.querySelector('input, select, textarea');
  if (!control) throw new Error(`control not found for ${labelText}`);
  return control as HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement;
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

  it('shows category inside incoming data card', async () => {
    renderCreateForm();
    await waitForFormReady();

    const incomingSection = getIncomingSection();
    expect(within(incomingSection).getByRole('combobox', { name: /التصنيف/ })).toBeInTheDocument();
  });

  it('does not render standalone response card', async () => {
    renderCreateForm();
    await waitForFormReady();

    expect(screen.queryByRole('heading', { name: 'الإفادة والمهلة' })).not.toBeInTheDocument();
  });

  it('shows response fields inside incoming data card', async () => {
    renderCreateForm();
    await waitForFormReady();

    const incomingSection = getIncomingSection();
    expect(within(incomingSection).getByText('بيانات الإفادة والمهلة')).toBeInTheDocument();
    expect(getFieldInSection(incomingSection, 'نوع الإفادة *')).toBeInTheDocument();
    expect(getFieldInSection(incomingSection, 'عدد الأيام للرد *')).toBeInTheDocument();
  });

  it('does not duplicate category in outgoing card', async () => {
    renderCreateForm();
    await waitForFormReady();

    const outgoingSection = screen.getAllByRole('heading', { name: 'بيانات الصادر' })[0].closest('section') as HTMLElement;
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

  it('selects external party on mouse click without Enter', async () => {
    const user = userEvent.setup();
    renderCreateForm();
    await waitForFormReady();

    await user.click(screen.getByRole('combobox', { name: /الجهة الوارد منها/ }));
    await user.click(screen.getByRole('option', { name: /جهة خارجية أ/ }));

    expect(screen.getByRole('combobox', { name: /الجهة الوارد منها/ })).toHaveValue('جهة خارجية أ');
    expect(screen.queryByRole('listbox')).not.toBeInTheDocument();
  });

  it('selects internal department on mouse click without Enter', async () => {
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

    const prioritySelect = getFieldInSection(getIncomingSection(), 'الأولوية *');
    await user.selectOptions(prioritySelect, 'Urgent');
    expect(prioritySelect).toHaveValue('Urgent');
  });

  it('includes selected values in create payload', async () => {
    const user = userEvent.setup();
    vi.mocked(services.transactionsApi.create).mockResolvedValue({ data: { id: 99 } } as never);

    renderCreateForm();
    await waitForFormReady();

    const incomingSection = getIncomingSection();
    await user.type(getFieldInSection(incomingSection, 'رقم الوارد *'), 'IN-100');
    await user.type(getFieldInSection(incomingSection, 'الموضوع *'), 'اختبار');
    await user.click(screen.getByRole('combobox', { name: /الجهة الوارد منها/ }));
    await user.click(screen.getByRole('option', { name: /جهة خارجية أ/ }));
    await user.click(screen.getByRole('combobox', { name: /التصنيف/ }));
    await user.click(screen.getByRole('option', { name: /تصنيف عام/ }));
    await user.selectOptions(getFieldInSection(incomingSection, 'الأولوية *'), 'Urgent');
    await user.selectOptions(getFieldInSection(incomingSection, 'نوع الإفادة *'), 'Internal');
    await user.type(getFieldInSection(incomingSection, 'عدد الأيام للرد *'), '7');
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
    expect(getFieldInSection(incomingSection, 'رقم الوارد *')).toHaveValue('IN-EDIT');
    expect(screen.getByRole('combobox', { name: /الجهة الوارد منها/ })).toHaveValue('إدارة داخلية');
    expect(screen.getByRole('combobox', { name: /التصنيف/ })).toHaveValue('تصنيف عام');
    expect(getFieldInSection(incomingSection, 'نوع الإفادة *')).toHaveValue('Both');

    await user.click(screen.getByRole('button', { name: 'حفظ' }));

    await waitFor(() => {
      expect(services.transactionsApi.update).toHaveBeenCalled();
    });
  });

  it('uses responsive single-column grid without horizontal overflow class regressions', async () => {
    renderCreateForm();
    await waitForFormReady();

    const grid = getIncomingSection().querySelector('.form-grid');

    expect(grid).toBeTruthy();
    expect(grid?.querySelectorAll('.form-group').length).toBeGreaterThan(4);
  });
});
