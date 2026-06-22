import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import ReportsPage from './Reports';
import * as services from '../api/services';

const emptyPage = {
  items: [],
  page: 1,
  pageSize: 5,
  totalCount: 0,
  totalPages: 0,
  hasNextPage: false,
  hasPreviousPage: false,
};

vi.mock('../api/services', () => ({
  reportsApi: {
    pageSummary: vi.fn(),
    openDetails: vi.fn(),
    responseRequiredDetails: vi.fn(),
    overdueResponsesDetails: vi.fn(),
    openAssignmentsDetails: vi.fn(),
    partialRepliesDetails: vi.fn(),
    overdueDetails: vi.fn(),
    waitingReplyDetails: vi.fn(),
    byCategory: vi.fn(),
    byIncomingParty: vi.fn(),
    byOutgoingDepartment: vi.fn(),
    departmentSummary: vi.fn(),
    exportExcel: vi.fn(),
    exportDepartmentIncomingClosedExcel: vi.fn(),
    exportDepartmentIncomingClosedPdf: vi.fn(),
    monthly: vi.fn(),
  },
  categoriesApi: { getAll: vi.fn() },
  departmentsApi: { getAll: vi.fn() },
}));

describe('ReportsPage auto loading', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    class MockIntersectionObserver {
      observe = vi.fn();
      disconnect = vi.fn();
    }
    vi.stubGlobal('IntersectionObserver', MockIntersectionObserver);
    vi.mocked(services.categoriesApi.getAll).mockResolvedValue({ data: [] } as never);
    vi.mocked(services.departmentsApi.getAll).mockResolvedValue({ data: [] } as never);
    vi.mocked(services.reportsApi.pageSummary).mockResolvedValue({
      data: {
        open: 0,
        responseRequired: 0,
        overdueResponses: 0,
        openAssignments: 0,
        partialReplies: 0,
        overdue: 0,
        waitingReply: 0,
      },
    } as never);
    vi.mocked(services.reportsApi.openDetails).mockResolvedValue({ data: emptyPage } as never);
    vi.mocked(services.reportsApi.byCategory).mockResolvedValue({ data: [] } as never);
    vi.mocked(services.reportsApi.byIncomingParty).mockResolvedValue({ data: [] } as never);
    vi.mocked(services.reportsApi.byOutgoingDepartment).mockResolvedValue({ data: [] } as never);
    vi.mocked(services.reportsApi.departmentSummary).mockResolvedValue({ data: [] } as never);
  });

  afterEach(() => {
    cleanup();
  });

  function renderReports(path = '/reports') {
    return render(
      <MemoryRouter initialEntries={[path]}>
        <ReportsPage />
      </MemoryRouter>,
    );
  }

  it('loads default open tab once on first open', async () => {
    renderReports('/reports');

    await waitFor(() => {
      expect(services.reportsApi.openDetails).toHaveBeenCalled();
    });
    expect(services.reportsApi.openDetails).toHaveBeenCalledTimes(1);
  });

  it('debounces search before reloading tab data', async () => {
    const user = userEvent.setup();
    renderReports('/reports?tab=open');

    await waitFor(() => expect(services.reportsApi.openDetails).toHaveBeenCalledTimes(1));

    const callsBefore = vi.mocked(services.reportsApi.openDetails).mock.calls.length;
    await user.type(screen.getByPlaceholderText(/بحث/), 'abc');
    expect(vi.mocked(services.reportsApi.openDetails).mock.calls.length).toBe(callsBefore);

    await waitFor(
      () => expect(vi.mocked(services.reportsApi.openDetails).mock.calls.length).toBeGreaterThan(callsBefore),
      { timeout: 2000 },
    );
  });

  it('resets filters and clears search field', async () => {
    const user = userEvent.setup();
    renderReports('/reports?tab=open');

    await waitFor(() => expect(services.reportsApi.openDetails).toHaveBeenCalled());
    await user.type(screen.getByPlaceholderText(/بحث/), 'test');
    await user.click(screen.getByRole('button', { name: 'إعادة تعيين الفلاتر' }));

    expect(screen.getByPlaceholderText(/بحث/)).toHaveValue('');
  });
});
