import { describe, expect, it, vi, beforeEach, afterEach } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import DashboardPage from './Dashboard';
import * as services from '../api/services';

vi.mock('../api/services', () => ({
  dashboardApi: {
    summary: vi.fn(),
    actionRequired: vi.fn(),
    topOverdueDepartments: vi.fn(),
    topIncomingParties: vi.fn(),
    categoryDistribution: vi.fn(),
    statusDistribution: vi.fn(),
  },
}));

describe('DashboardPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(services.dashboardApi.summary).mockResolvedValue({
      data: { totalOpen: 0, requiresResponsePending: 0, responseOverdueCount: 0, waitingForReply: 0, partiallyReplied: 0, readyForResponse: 0, closedThisMonth: 0, averageCompletionDays: 0 },
    } as never);
    vi.mocked(services.dashboardApi.actionRequired).mockResolvedValue({ data: [] } as never);
    vi.mocked(services.dashboardApi.topOverdueDepartments).mockResolvedValue({ data: [] } as never);
    vi.mocked(services.dashboardApi.topIncomingParties).mockResolvedValue({ data: [] } as never);
    vi.mocked(services.dashboardApi.categoryDistribution).mockResolvedValue({ data: [] } as never);
    vi.mocked(services.dashboardApi.statusDistribution).mockResolvedValue({ data: [] } as never);
  });

  afterEach(() => {
    cleanup();
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
