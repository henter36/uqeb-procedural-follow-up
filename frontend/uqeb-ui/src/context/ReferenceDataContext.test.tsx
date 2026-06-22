import { afterEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import { ReferenceDataProvider } from './ReferenceDataProvider';
import { useReferenceData } from '../hooks/useReferenceData';
import * as services from '../api/services';

vi.mock('../api/services', () => ({
  departmentsApi: { getAll: vi.fn() },
  categoriesApi: { getAll: vi.fn() },
  externalPartiesApi: { getAll: vi.fn() },
}));

function Probe() {
  const { departments, loading, error, refresh } = useReferenceData();
  return (
    <div>
      <span data-testid="loading">{loading ? 'yes' : 'no'}</span>
      <span data-testid="count">{departments.length}</span>
      <span data-testid="error">{error ?? ''}</span>
      <button type="button" onClick={refresh}>refresh</button>
    </div>
  );
}

describe('ReferenceDataProvider', () => {
  afterEach(() => {
    cleanup();
    vi.clearAllMocks();
  });

  it('loads reference data once per provider mount', async () => {
    vi.mocked(services.departmentsApi.getAll).mockResolvedValue({ data: [{ id: 1, name: 'أ', isActive: true }] } as never);
    vi.mocked(services.categoriesApi.getAll).mockResolvedValue({ data: [] } as never);
    vi.mocked(services.externalPartiesApi.getAll).mockResolvedValue({ data: [] } as never);

    render(
      <ReferenceDataProvider>
        <Probe />
      </ReferenceDataProvider>,
    );

    await waitFor(() => expect(screen.getByTestId('count')).toHaveTextContent('1'));
    expect(services.departmentsApi.getAll).toHaveBeenCalledTimes(1);
  });

  it('retries successfully after initial failure', async () => {
    vi.mocked(services.departmentsApi.getAll)
      .mockRejectedValueOnce(new Error('fail'))
      .mockResolvedValue({ data: [{ id: 2, name: 'ب', isActive: true }] } as never);
    vi.mocked(services.categoriesApi.getAll).mockResolvedValue({ data: [] } as never);
    vi.mocked(services.externalPartiesApi.getAll).mockResolvedValue({ data: [] } as never);

    const { unmount } = render(
      <ReferenceDataProvider>
        <Probe />
      </ReferenceDataProvider>,
    );

    await waitFor(() => expect(screen.getByTestId('error')).toHaveTextContent('تعذر تحميل البيانات المرجعية'));
    unmount();

    render(
      <ReferenceDataProvider>
        <Probe />
      </ReferenceDataProvider>,
    );

    await waitFor(() => expect(screen.getByTestId('count')).toHaveTextContent('1'));
  });

  it('refreshes data on demand', async () => {
    vi.mocked(services.departmentsApi.getAll).mockResolvedValue({ data: [{ id: 1, name: 'أ', isActive: true }] } as never);
    vi.mocked(services.categoriesApi.getAll).mockResolvedValue({ data: [] } as never);
    vi.mocked(services.externalPartiesApi.getAll).mockResolvedValue({ data: [] } as never);

    render(
      <ReferenceDataProvider>
        <Probe />
      </ReferenceDataProvider>,
    );

    await waitFor(() => expect(screen.getByTestId('loading')).toHaveTextContent('no'));
    screen.getByRole('button', { name: 'refresh' }).click();
    await waitFor(() => expect(services.departmentsApi.getAll).toHaveBeenCalledTimes(2));
  });
});
