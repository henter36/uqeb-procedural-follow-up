import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import TransactionsList from './TransactionsList';
import * as services from '../api/services';
import * as safeStorage from '../utils/safeStorage';

vi.mock('../context/useAuth', () => ({
  useAuth: () => ({
    canEdit: true,
    isAdmin: false,
    user: null,
    logout: vi.fn(),
    login: vi.fn(),
    canClose: false,
    isDepartmentUser: false,
  }),
}));

vi.mock('../api/services', () => ({
  transactionsApi: { search: vi.fn() },
  departmentsApi: { getAll: vi.fn() },
  categoriesApi: { getAll: vi.fn() },
  externalPartiesApi: { getAll: vi.fn() },
}));

vi.mock('../utils/safeStorage', () => ({
  getStorageItem: vi.fn(() => null),
  setStorageItem: vi.fn(() => true),
  removeStorageItem: vi.fn(() => true),
}));

describe('TransactionsList', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(services.departmentsApi.getAll).mockResolvedValue({ data: [] } as never);
    vi.mocked(services.categoriesApi.getAll).mockResolvedValue({ data: [] } as never);
    vi.mocked(services.externalPartiesApi.getAll).mockResolvedValue({ data: [] } as never);
  });

  afterEach(() => {
    cleanup();
  });

  it('TransactionsPage_HasSingleMainHeading', async () => {
    vi.mocked(services.transactionsApi.search).mockResolvedValue({ data: { items: [], totalCount: 0 } } as never);

    render(
      <MemoryRouter>
        <TransactionsList />
      </MemoryRouter>,
    );

    await waitFor(() => {
      expect(screen.getByText('لا توجد معاملات نشطة.')).toBeInTheDocument();
    });

    expect(screen.getAllByRole('heading', { name: 'المعاملات' })).toHaveLength(1);
    expect(screen.queryByText('بحث وفلترة وإدارة جميع المعاملات')).not.toBeInTheDocument();
  });

  it('defaults to the active transactions tab and sends active statusScope', async () => {
    vi.mocked(services.transactionsApi.search).mockResolvedValue({ data: { items: [], totalCount: 0 } } as never);

    render(
      <MemoryRouter>
        <TransactionsList />
      </MemoryRouter>,
    );

    await waitFor(() => {
      expect(services.transactionsApi.search).toHaveBeenCalledWith(expect.objectContaining({
        statusScope: 'active',
      }));
    });

    expect(screen.getByRole('tab', { name: 'النشطة' })).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByText('لا توجد معاملات نشطة.')).toBeInTheDocument();
  });

  it('sends closed statusScope and clears status filter when the closed tab is selected', async () => {
    const user = userEvent.setup();
    vi.mocked(services.transactionsApi.search).mockResolvedValue({ data: { items: [], totalCount: 0 } } as never);

    render(
      <MemoryRouter>
        <TransactionsList />
      </MemoryRouter>,
    );

    await waitFor(() => {
      expect(screen.getByText('لا توجد معاملات نشطة.')).toBeInTheDocument();
    });

    const statusSelect = screen.getByLabelText('الحالة') as HTMLSelectElement;
    await user.selectOptions(statusSelect, 'New');
    expect(statusSelect.value).toBe('New');

    await user.click(screen.getByRole('tab', { name: 'المغلقة' }));

    await waitFor(() => {
      expect(services.transactionsApi.search).toHaveBeenLastCalledWith(expect.objectContaining({
        statusScope: 'closed',
      }));
    });
    expect(statusSelect.value).toBe('');
    expect(safeStorage.setStorageItem).toHaveBeenLastCalledWith(
      'uqeb-transaction-filters',
      expect.stringContaining('"status":""'),
    );
    expect(screen.getByText('لا توجد معاملات مغلقة.')).toBeInTheDocument();
  });

  it('sends all statusScope when the all tab is selected', async () => {
    const user = userEvent.setup();
    vi.mocked(services.transactionsApi.search).mockResolvedValue({ data: { items: [], totalCount: 0 } } as never);

    render(
      <MemoryRouter>
        <TransactionsList />
      </MemoryRouter>,
    );

    await waitFor(() => {
      expect(screen.getByText('لا توجد معاملات نشطة.')).toBeInTheDocument();
    });

    await user.click(screen.getByRole('tab', { name: 'الكل' }));

    await waitFor(() => {
      expect(services.transactionsApi.search).toHaveBeenLastCalledWith(expect.objectContaining({
        statusScope: 'all',
      }));
    });
    expect(screen.getByText('لا توجد معاملات مطابقة.')).toBeInTheDocument();
  });

  it('shows ErrorState when search fails', async () => {
    vi.mocked(services.transactionsApi.search).mockRejectedValue(new Error('network'));

    render(
      <MemoryRouter>
        <TransactionsList />
      </MemoryRouter>,
    );

    await waitFor(() => {
      expect(screen.getByRole('alert')).toHaveTextContent('تعذر تحميل المعاملات');
    });
    expect(screen.queryByText('لا توجد معاملات')).not.toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'إعادة المحاولة' })).toBeInTheDocument();
  });

  it('retries loading after search failure', async () => {
    const user = userEvent.setup();
    vi.mocked(services.transactionsApi.search)
      .mockRejectedValueOnce(new Error('network'))
      .mockResolvedValueOnce({ data: { items: [], totalCount: 0 } } as never);

    render(
      <MemoryRouter>
        <TransactionsList />
      </MemoryRouter>,
    );

    await waitFor(() => {
      expect(screen.getByRole('button', { name: 'إعادة المحاولة' })).toBeInTheDocument();
    });

    await user.click(screen.getByRole('button', { name: 'إعادة المحاولة' }));

    await waitFor(() => {
      expect(screen.getByText('لا توجد معاملات نشطة.')).toBeInTheDocument();
    });
  });

  it('survives localStorage failures during search and reset', async () => {
    vi.mocked(services.transactionsApi.search).mockResolvedValue({ data: { items: [], totalCount: 0 } } as never);
    vi.mocked(safeStorage.setStorageItem).mockReturnValue(false);
    vi.mocked(safeStorage.removeStorageItem).mockReturnValue(false);

    const user = userEvent.setup();
    render(
      <MemoryRouter>
        <TransactionsList />
      </MemoryRouter>,
    );

    await waitFor(() => {
      expect(screen.getByText('لا توجد معاملات نشطة.')).toBeInTheDocument();
    });

    await user.click(screen.getByRole('button', { name: 'بحث' }));
    expect(safeStorage.setStorageItem).toHaveBeenCalled();

    await user.click(screen.getByRole('button', { name: 'إعادة ضبط' }));
    expect(safeStorage.removeStorageItem).toHaveBeenCalled();
  });

  it('preserves current search filters when searching from the active tab', async () => {
    const user = userEvent.setup();
    vi.mocked(services.transactionsApi.search).mockResolvedValue({ data: { items: [], totalCount: 0 } } as never);

    render(
      <MemoryRouter>
        <TransactionsList />
      </MemoryRouter>,
    );

    await waitFor(() => {
      expect(screen.getByText('لا توجد معاملات نشطة.')).toBeInTheDocument();
    });

    await user.type(screen.getByLabelText('الموضوع'), 'اختبار');
    await user.click(screen.getByRole('button', { name: 'بحث' }));

    await waitFor(() => {
      expect(services.transactionsApi.search).toHaveBeenLastCalledWith(expect.objectContaining({
        statusScope: 'active',
        subject: 'اختبار',
      }));
    });
  });

  it('shows a recurring badge for transactions linked to a recurring template', async () => {
    vi.mocked(services.transactionsApi.search).mockResolvedValue({
      data: {
        items: [
          {
            id: 1,
            incomingNumber: 'IN-1',
            incomingDate: '2026-07-01',
            subject: 'معاملة دورية',
            incomingSourceType: 'Internal',
            incomingFrom: 'إدارة',
            categoryName: 'تصنيف',
            outgoingDepartmentNames: [],
            status: 'New',
            recurringTemplateId: 5,
            recurringPeriodLabel: 'يوليو 2026',
          },
          {
            id: 2,
            incomingNumber: 'IN-2',
            incomingDate: '2026-07-01',
            subject: 'معاملة عادية',
            incomingSourceType: 'Internal',
            incomingFrom: 'إدارة',
            categoryName: 'تصنيف',
            outgoingDepartmentNames: [],
            status: 'New',
          },
        ],
        totalCount: 2,
      },
    } as never);

    render(
      <MemoryRouter>
        <TransactionsList />
      </MemoryRouter>,
    );

    await waitFor(() => {
      expect(screen.getByText('معاملة دورية')).toBeInTheDocument();
    });

    expect(screen.getByText('دورية')).toBeInTheDocument();
    const regularRow = screen.getByText('معاملة عادية').closest('tr');
    expect(regularRow).not.toBeNull();
    expect(regularRow?.textContent).not.toContain('دورية فقط');
  });

  it('sends recurring filters when selected in the advanced filters panel', async () => {
    const user = userEvent.setup();
    vi.mocked(services.transactionsApi.search).mockResolvedValue({ data: { items: [], totalCount: 0 } } as never);

    render(
      <MemoryRouter>
        <TransactionsList />
      </MemoryRouter>,
    );

    await waitFor(() => {
      expect(screen.getByText('لا توجد معاملات نشطة.')).toBeInTheDocument();
    });

    await user.click(screen.getByRole('button', { name: 'فلاتر متقدمة' }));

    await user.selectOptions(screen.getByLabelText('معاملات ذات التزام دوري'), 'true');
    await user.selectOptions(screen.getByLabelText('نوع التكرار'), 'Monthly');
    await user.selectOptions(screen.getByLabelText('حالة الالتزام'), 'Active');
    await user.click(screen.getByRole('button', { name: 'بحث' }));

    await waitFor(() => {
      expect(services.transactionsApi.search).toHaveBeenLastCalledWith(expect.objectContaining({
        isRecurring: true,
        recurringRecurrenceType: 'Monthly',
        recurringTemplateStatus: 'Active',
      }));
    });
  });
});
