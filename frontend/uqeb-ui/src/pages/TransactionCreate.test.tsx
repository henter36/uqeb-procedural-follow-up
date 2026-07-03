import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
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

function renderCreateForm() {
  return render(
    <MemoryRouter initialEntries={['/transactions/new']}>
      <Routes>
        <Route path="/transactions/new" element={<TransactionForm mode="create" />} />
      </Routes>
    </MemoryRouter>,
  );
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

async function waitForFormReady() {
  await waitFor(() => {
    expect(screen.queryByText('جاري التحميل...')).not.toBeInTheDocument();
  });
}

describe('TransactionCreate', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(services.externalPartiesApi.getAll).mockResolvedValue({
      data: [{ id: 10, name: 'جهة خارجية أ', type: 'Gov', isActive: true }],
    } as never);
    vi.mocked(services.departmentsApi.getAll).mockResolvedValue({ data: [] } as never);
    vi.mocked(services.categoriesApi.getAll).mockResolvedValue({
      data: [{ id: 30, name: 'تصنيف عام', code: 'CAT-01', isActive: true }],
    } as never);
  });

  afterEach(() => {
    cleanup();
  });

  it('does not prefill transaction date on create', async () => {
    renderCreateForm();
    await waitForFormReady();

    expect(getFieldInSection(getIncomingSection(), 'تاريخ المعاملة *')).toHaveValue('');
  });

  it('shows Arabic validation when saving without transaction date', async () => {
    const user = userEvent.setup();
    renderCreateForm();
    await waitForFormReady();

    const incomingSection = getIncomingSection();
    await user.type(getFieldInSection(incomingSection, 'رقم المعاملة *'), 'IN-100');
    await user.type(getFieldInSection(incomingSection, 'الموضوع *'), 'اختبار');
    await user.click(screen.getByRole('combobox', { name: /الجهة الوارد منها/ }));
    await user.click(screen.getByRole('option', { name: /جهة خارجية أ/ }));
    await user.click(screen.getByRole('combobox', { name: /التصنيف/ }));
    await user.click(screen.getByRole('option', { name: /تصنيف عام/ }));
    await user.type(getFieldInSection(incomingSection, 'عدد الأيام للرد *'), '7');
    await user.click(screen.getByRole('button', { name: 'حفظ' }));

    expect(screen.getByRole('alert')).toHaveTextContent('تاريخ المعاملة مطلوب.');
    expect(services.transactionsApi.create).not.toHaveBeenCalled();
  });
});
