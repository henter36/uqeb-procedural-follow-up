import { describe, expect, it, vi, beforeEach } from 'vitest';
import { render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import TransactionForm from './TransactionForm';
import * as services from '../api/services';

vi.mock('../api/services', () => ({
  transactionsApi: { getById: vi.fn() },
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

describe('TransactionForm bootstrap', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('loads reference data successfully in create mode', async () => {
    vi.mocked(services.externalPartiesApi.getAll).mockResolvedValue({ data: [] } as never);
    vi.mocked(services.departmentsApi.getAll).mockResolvedValue({ data: [] } as never);
    vi.mocked(services.categoriesApi.getAll).mockResolvedValue({ data: [] } as never);

    renderCreateForm();

    await waitFor(() => {
      expect(screen.queryByText('جاري التحميل...')).not.toBeInTheDocument();
    });

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
