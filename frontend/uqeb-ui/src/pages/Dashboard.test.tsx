import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
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

const mockUseAuth = vi.fn(() => ({ canClose: false, canOperateFollowUpPrint: false, isDepartmentUser: false }));

vi.mock('../context/useAuth', () => ({
  useAuth: () => mockUseAuth(),
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
    mockUseAuth.mockReturnValue({ canClose: false, canOperateFollowUpPrint: false, isDepartmentUser: false });
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

  it('does not render removed operational KPI cards', async () => {
    render(
      <MemoryRouter>
        <DashboardPage />
      </MemoryRouter>,
    );

    await waitFor(() => {
      expect(screen.getByText('لا توجد معاملات تحتاج إجراء')).toBeInTheDocument();
    });

    expect(screen.queryByText('معاملات بلا تحديث حديث')).not.toBeInTheDocument();
    expect(screen.queryByText('مطلوب إفادة')).not.toBeInTheDocument();
    expect(screen.queryByText(/مفتوحة تتطلب ردًا/)).not.toBeInTheDocument();
  });

  it('redirects a DepartmentUser to their own department page instead of fetching institution-wide counts', async () => {
    mockUseAuth.mockReturnValue({ canClose: false, canOperateFollowUpPrint: false, isDepartmentUser: true });

    render(
      <MemoryRouter initialEntries={['/']}>
        <Routes>
          <Route path="/" element={<DashboardPage />} />
          <Route path="/department-responses" element={<div>معاملات إدارتي</div>} />
        </Routes>
      </MemoryRouter>,
    );

    await waitFor(() => {
      expect(screen.getByText('معاملات إدارتي')).toBeInTheDocument();
    });

    expect(services.reportsApi.dashboard).not.toHaveBeenCalled();
  });
});
