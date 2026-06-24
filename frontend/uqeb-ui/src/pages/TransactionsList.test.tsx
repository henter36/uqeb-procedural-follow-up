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
      expect(screen.getByText('لا توجد معاملات')).toBeInTheDocument();
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
      expect(screen.getByText('لا توجد معاملات')).toBeInTheDocument();
    });

    await user.click(screen.getByRole('button', { name: 'بحث' }));
    expect(safeStorage.setStorageItem).toHaveBeenCalled();

    await user.click(screen.getByRole('button', { name: 'إعادة ضبط' }));
    expect(safeStorage.removeStorageItem).toHaveBeenCalled();
  });
});
