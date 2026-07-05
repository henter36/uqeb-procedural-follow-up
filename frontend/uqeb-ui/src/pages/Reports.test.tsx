import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { AxiosResponse } from 'axios';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import ReportsPage from './Reports';
import * as services from '../api/services';
import type { ReportSectionCounts, RecurringObligationsSummary, DepartmentObligationSnapshot } from '../api/types';

const emptyPage = {
  items: [],
  page: 1,
  pageSize: 5,
  totalCount: 0,
  totalPages: 0,
  hasNextPage: false,
  hasPreviousPage: false,
};

function mockAxiosResponse<T>(data: T): AxiosResponse<T> {
  return {
    data,
    status: 200,
    statusText: 'OK',
    headers: {},
    config: { headers: {} } as AxiosResponse<T>['config'],
  };
}

const emptySummary: ReportSectionCounts = {
  open: 0,
  responseRequired: 0,
  overdueResponses: 0,
  openAssignments: 0,
  partialReplies: 0,
  overdue: 0,
  waitingReply: 0,
};

const emptyRecurringSummary: RecurringObligationsSummary = {
  total: 0,
  active: 0,
  upcoming: 0,
  dueSoon: 0,
  overdue: 0,
  suspended: 0,
  terminated: 0,
  groups: [],
};

const emptyDeptSnapshot: DepartmentObligationSnapshot = {
  totalDepartmentsInScope: 0,
  totalDistinctObligations: 0,
  multiDepartmentObligationsCount: 0,
  departments: [],
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
    recurringObligationsSummary: vi.fn(),
    recurringObligationsDetails: vi.fn(),
    exportRecurringObligationsExcel: vi.fn(),
    departmentObligationSnapshot: vi.fn(),
    exportDepartmentObligationSnapshotExcel: vi.fn(),
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
    vi.mocked(services.categoriesApi.getAll).mockResolvedValue(mockAxiosResponse([]));
    vi.mocked(services.departmentsApi.getAll).mockResolvedValue(mockAxiosResponse([]));
    vi.mocked(services.reportsApi.pageSummary).mockResolvedValue(mockAxiosResponse(emptySummary));
    vi.mocked(services.reportsApi.openDetails).mockResolvedValue(mockAxiosResponse(emptyPage));
    vi.mocked(services.reportsApi.byCategory).mockResolvedValue(mockAxiosResponse([]));
    vi.mocked(services.reportsApi.byIncomingParty).mockResolvedValue(mockAxiosResponse([]));
    vi.mocked(services.reportsApi.byOutgoingDepartment).mockResolvedValue(mockAxiosResponse([]));
    vi.mocked(services.reportsApi.departmentSummary).mockResolvedValue(mockAxiosResponse([]));
    vi.mocked(services.reportsApi.recurringObligationsSummary).mockResolvedValue(mockAxiosResponse(emptyRecurringSummary));
    vi.mocked(services.reportsApi.recurringObligationsDetails).mockResolvedValue(mockAxiosResponse(emptyPage));
    vi.mocked(services.reportsApi.departmentObligationSnapshot).mockResolvedValue(mockAxiosResponse(emptyDeptSnapshot));
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
    expect(vi.mocked(services.reportsApi.openDetails).mock.calls).toHaveLength(callsBefore);

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

  it('loads the recurring obligations summary and details on mount', async () => {
    vi.mocked(services.reportsApi.recurringObligationsSummary).mockResolvedValue(mockAxiosResponse({
      ...emptyRecurringSummary,
      total: 4,
      active: 3,
      overdue: 1,
    }));
    vi.mocked(services.reportsApi.recurringObligationsDetails).mockResolvedValue(mockAxiosResponse({
      ...emptyPage,
      items: [{
        templateId: 1,
        title: 'التزام اختبار',
        owningDepartmentName: 'إدارة أ',
        responsibleDepartmentNames: [],
        recurrenceType: 'Monthly',
        recurrenceTypeLabel: 'شهري',
        startDate: '2026-01-01',
        nextDueDate: '2026-08-01',
        status: 'Active',
        scheduleStatus: 'Overdue',
        daysRemaining: -5,
        priority: 'Normal',
        generatedTransactionsCount: 2,
      }],
      totalCount: 1,
    }));

    renderReports('/reports');

    await waitFor(() => {
      expect(services.reportsApi.recurringObligationsSummary).toHaveBeenCalled();
      expect(services.reportsApi.recurringObligationsDetails).toHaveBeenCalled();
    });

    expect(await screen.findByText('التزام اختبار')).toBeInTheDocument();
    expect(screen.getByText('تقرير الالتزامات الدورية')).toBeInTheDocument();
  });

  it('loads the department obligation snapshot on mount and separates owner from responsible counts', async () => {
    vi.mocked(services.reportsApi.departmentObligationSnapshot).mockResolvedValue(mockAxiosResponse({
      totalDepartmentsInScope: 2,
      totalDistinctObligations: 3,
      multiDepartmentObligationsCount: 1,
      departments: [
        {
          departmentId: 10,
          departmentName: 'الإدارة أ',
          ownedCount: 2,
          responsibleCount: 0,
          referredCount: 0,
          openActionCount: 0,
          pendingActionCount: 0,
          completedActionCount: 0,
          submittedResponseCount: 0,
          approvedResponseCount: 0,
          overdueCount: 0,
          dueSoonCount: 0,
          attributionMismatchCount: 0,
          involvementCategory: 'OwnerOnly',
        },
        {
          departmentId: 20,
          departmentName: 'الإدارة ب',
          ownedCount: 0,
          responsibleCount: 2,
          referredCount: 1,
          openActionCount: 1,
          pendingActionCount: 1,
          completedActionCount: 1,
          submittedResponseCount: 1,
          approvedResponseCount: 1,
          overdueCount: 1,
          dueSoonCount: 0,
          attributionMismatchCount: 0,
          involvementCategory: 'ResponsibleOrReferredOnly',
        },
      ],
    }));

    renderReports('/reports');

    await waitFor(() => {
      expect(services.reportsApi.departmentObligationSnapshot).toHaveBeenCalled();
    });

    expect(await screen.findByText('لقطة التزامات الإدارات')).toBeInTheDocument();
    expect(screen.getByText('الإدارة أ')).toBeInTheDocument();
    expect(screen.getByText('الإدارة ب')).toBeInTheDocument();
    expect(screen.getByText('مالكة فقط')).toBeInTheDocument();
    expect(screen.getByText('مسؤولة/محالة فقط')).toBeInTheDocument();
  });
});
