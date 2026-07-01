import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import DashboardPage from './Dashboard';
import * as services from '../api/services';

vi.mock('../api/services', () => ({
  reportsApi: {
    dashboard: vi.fn(),
  },
  dashboardApi: {
    summary: vi.fn(),
    actionRequired: vi.fn(),
    topOverdueDepartments: vi.fn(),
    topIncomingParties: vi.fn(),
    categoryDistribution: vi.fn(),
    statusDistribution: vi.fn(),
  },
}));

vi.mock('../hooks/usePendingPrintSummary', () => ({
  usePendingPrintSummary: () => ({ pendingTotal: 0, refresh: vi.fn() }),
}));

vi.mock('../context/useAuth', () => ({
  useAuth: () => ({ canClose: false, canOperateFollowUpPrint: false }),
}));

const emptyDashboard = {
  totalOpen: 0,
  requiresResponsePending: 0,
  responseOverdueCount: 0,
  waitingForReply: 0,
  partiallyReplied: 0,
  readyForResponse: 0,
  closedThisMonth: 0,
  averageCompletionDays: 0,
  topOverdueDepartments: [],
  topIncomingParties: [],
  byCategory: [],
  byStatus: [],
  actionRequired: [],
};

describe('DashboardPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(services.reportsApi.dashboard).mockResolvedValue({ data: emptyDashboard } as never);
  });

  afterEach(() => {
    cleanup();
  });

  it('loads consolidated dashboard in one request', async () => {
    render(
      <MemoryRouter>
        <DashboardPage />
      </MemoryRouter>,
    );

    await waitFor(() => {
      expect(services.reportsApi.dashboard).toHaveBeenCalledTimes(1);
    });

    expect(services.dashboardApi.summary).not.toHaveBeenCalled();
    expect(services.dashboardApi.actionRequired).not.toHaveBeenCalled();
  });

  it('Dashboard_HasSingleMainHeading', async () => {
    render(
      <MemoryRouter>
        <DashboardPage />
      </MemoryRouter>,
    );

    await waitFor(() => {
      expect(screen.getByText('لا توجد معاملات تحتاج إجراء')).toBeInTheDocument();
    });

    expect(screen.getAllByRole('heading', { name: 'لوحة المتابعة' })).toHaveLength(1);
    expect(screen.queryByText('نظرة تنفيذية على المعاملات والتعقيبات')).not.toBeInTheDocument();
  });

  it('shows a single empty state for action required section', async () => {
    render(
      <MemoryRouter>
        <DashboardPage />
      </MemoryRouter>,
    );

    await waitFor(() => {
      expect(screen.getByText('لا توجد معاملات تحتاج إجراء')).toBeInTheDocument();
    });

    expect(screen.queryAllByText('لا توجد معاملات تحتاج إجراء')).toHaveLength(1);
    expect(screen.queryByRole('row', { name: /لا توجد/ })).not.toBeInTheDocument();
  });
});
