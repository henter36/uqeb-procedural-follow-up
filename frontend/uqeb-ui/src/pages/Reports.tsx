import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { isAxiosError } from 'axios';
import { reportsApi, categoriesApi, departmentsApi } from '../api/services';
import type {
  ReportTransactionRow, Category, Department, DepartmentSummaryReport,
  OutgoingDepartmentReport, ReportSectionCounts, PagedResult,
  RecurringObligationsSummary, RecurringObligationReportRow, DepartmentObligationSnapshot,
} from '../api/types';
import {
  statusLabels, statusBadgeClass, priorityLabels,
  recurrenceTypeLabels, recurringTemplateStatusLabels, recurringScheduleStatusLabels,
  involvementCategoryLabels,
} from '../utils/labels';
import DateDisplay from '../components/DateDisplay';
import DepartmentBadges from '../components/DepartmentBadges';
import { PageHeader } from '../components/ui';
import { responseTimingBadgeClass } from '../utils/responseTiming';
import { useDebouncedValue } from '../hooks/useDebouncedValue';
import { downloadBlob } from '../utils/downloadBlob';
import { getAnalyticsStatusText, getAnalyticsViewState } from '../utils/reportsAnalytics';

const TIMING_REPORT_TABS: ReportTab[] = ['response-required', 'overdue-responses', 'waiting', 'open'];

type ReportTab = 'response-required' | 'overdue-responses' | 'pending-assignments' | 'partial-replies' | 'overdue' | 'open' | 'waiting';

type TabState = {
  items: ReportTransactionRow[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasNextPage: boolean;
  hasPreviousPage: boolean;
  loading: boolean;
  error: string | null;
  loaded: boolean;
  stale: boolean;
  fetchKey: string;
};

const PAGE_SIZE_OPTIONS = [5, 10, 50] as const;
const DEFAULT_PAGE_SIZE = 5;

const defaultTabState = (): TabState => ({
  items: [],
  page: 1,
  pageSize: DEFAULT_PAGE_SIZE,
  totalCount: 0,
  totalPages: 0,
  hasNextPage: false,
  hasPreviousPage: false,
  loading: false,
  error: null,
  loaded: false,
  stale: true,
  fetchKey: '',
});

const tabConfig: {
  key: ReportTab;
  label: string;
  countKey: keyof ReportSectionCounts;
  loader: (p?: Record<string, unknown>, config?: { signal?: AbortSignal }) => Promise<{ data: PagedResult<ReportTransactionRow> }>;
}[] = [
  { key: 'response-required', label: 'مطلوب إفادة', countKey: 'responseRequired', loader: reportsApi.responseRequiredDetails },
  { key: 'overdue-responses', label: 'متأخر في الإفادة', countKey: 'overdueResponses', loader: reportsApi.overdueResponsesDetails },
  { key: 'pending-assignments', label: 'احالةات مفتوحة', countKey: 'openAssignments', loader: reportsApi.openAssignmentsDetails },
  { key: 'partial-replies', label: 'ردود جزئية', countKey: 'partialReplies', loader: reportsApi.partialRepliesDetails },
  { key: 'overdue', label: 'المتأخرة', countKey: 'overdue', loader: reportsApi.overdueDetails },
  { key: 'waiting', label: 'بانتظار رد', countKey: 'waitingReply', loader: reportsApi.waitingReplyDetails },
  { key: 'open', label: 'المفتوحة', countKey: 'open', loader: reportsApi.openDetails },
];

function extractErrorMessage(err: unknown): string {
  if (isAxiosError(err)) {
    if (err.code === 'ERR_CANCELED') return '';
    const data = err.response?.data;
    if (typeof data === 'string' && data) return data;
    if (data && typeof data === 'object' && 'message' in data && typeof data.message === 'string') return data.message;
    return err.message || 'تعذر تحميل التقرير';
  }
  if (err instanceof Error) return err.message;
  return 'تعذر تحميل التقرير';
}

function buildFetchKey(tabKey: ReportTab, page: number, pageSize: number, filters: Record<string, unknown>) {
  return `${tabKey}|${page}|${pageSize}|${JSON.stringify(filters)}`;
}

function recurringScheduleStatusBadgeClass(scheduleStatus: string): string {
  if (scheduleStatus === 'Overdue') return 'badge-red';
  if (scheduleStatus === 'DueSoon') return 'badge-orange';
  return 'badge-blue';
}

function parseReportTab(value: string | null): ReportTab {
  if (!value) return 'open';
  return tabConfig.some((t) => t.key === value) ? (value as ReportTab) : 'open';
}

type TableSkeletonProps = Readonly<{
  rows?: number;
  columns?: number;
}>;

function createSkeletonKeys(prefix: string, count: number) {
  return Array.from({ length: count }, (_, index) => `${prefix}-${index + 1}`);
}

function TableSkeleton({ rows = 5, columns = 8 }: TableSkeletonProps) {
  const rowKeys = createSkeletonKeys('skeleton-row', rows);
  const columnKeys = createSkeletonKeys('skeleton-column', columns);

  return (
    <>
      {rowKeys.map((rowKey) => (
        <tr key={rowKey} className="skeleton-row">
          {columnKeys.map((columnKey) => (
            <td key={columnKey}><div className="skeleton-bar w-60" /></td>
          ))}
        </tr>
      ))}
    </>
  );
}

export default function ReportsPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const tab = parseReportTab(searchParams.get('tab'));
  const [tabStates, setTabStates] = useState<Record<ReportTab, TabState>>(() =>
    Object.fromEntries(tabConfig.map((t) => [t.key, defaultTabState()])) as Record<ReportTab, TabState>
  );
  const [categories, setCategories] = useState<Category[]>([]);
  const [departments, setDepartments] = useState<Department[]>([]);
  const [sectionCounts, setSectionCounts] = useState<ReportSectionCounts | null>(null);
  const [summaryReadyToken, setSummaryReadyToken] = useState('');
  const [summaryError, setSummaryError] = useState<string | null>(null);
  const [summaryRetryKey, setSummaryRetryKey] = useState(0);
  const [categoryReport, setCategoryReport] = useState<{ categoryName: string; count: number }[]>([]);
  const [incomingReport, setIncomingReport] = useState<{ partyName: string; transactionCount: number }[]>([]);
  const [outgoingDeptReport, setOutgoingDeptReport] = useState<OutgoingDepartmentReport[]>([]);
  const [deptSummary, setDeptSummary] = useState<DepartmentSummaryReport[]>([]);
  const [analyticsError, setAnalyticsError] = useState<string | null>(null);
  const [analyticsReadyKey, setAnalyticsReadyKey] = useState<string | null>(null);
  const [analyticsHasData, setAnalyticsHasData] = useState(false);
  const [analyticsUpdatedAt, setAnalyticsUpdatedAt] = useState<Date | null>(null);
  const [monthly, setMonthly] = useState<{ month: number; incomingCount: number; outgoingCount: number }[]>([]);
  const [monthlyLoaded, setMonthlyLoaded] = useState(false);
  const [year, setYear] = useState(new Date().getFullYear());
  const [draftFilters, setDraftFilters] = useState({
    dateFrom: '', dateTo: '', categoryId: '', departmentId: '', status: '', incomingSourceType: '', search: '',
  });
  const debouncedSearch = useDebouncedValue(draftFilters.search, 300);
  const appliedFilters = useMemo(() => ({
    dateFrom: draftFilters.dateFrom,
    dateTo: draftFilters.dateTo,
    categoryId: draftFilters.categoryId,
    departmentId: draftFilters.departmentId,
    status: draftFilters.status,
    incomingSourceType: draftFilters.incomingSourceType,
  }), [
    draftFilters.dateFrom,
    draftFilters.dateTo,
    draftFilters.categoryId,
    draftFilters.departmentId,
    draftFilters.status,
    draftFilters.incomingSourceType,
  ]);
  const abortRef = useRef<AbortController | null>(null);
  const summaryAbortRef = useRef<AbortController | null>(null);
  const monthlyRef = useRef<HTMLDivElement>(null);
  const tabStatesRef = useRef(tabStates);
  const analyticsRequestIdRef = useRef(0);
  const [exportError, setExportError] = useState<string | null>(null);
  const [exporting, setExporting] = useState(false);

  const [recurringFilters, setRecurringFilters] = useState({
    dateFrom: '', dateTo: '', departmentId: '', status: '', recurrenceType: '', priority: '', groupBy: '',
  });
  const [recurringSummary, setRecurringSummary] = useState<RecurringObligationsSummary | null>(null);
  const [recurringRows, setRecurringRows] = useState<RecurringObligationReportRow[]>([]);
  const [recurringPage, setRecurringPage] = useState(1);
  const [recurringPageSize, setRecurringPageSize] = useState(5);
  const [recurringTotalCount, setRecurringTotalCount] = useState(0);
  const [recurringTotalPages, setRecurringTotalPages] = useState(0);
  const [recurringHasNextPage, setRecurringHasNextPage] = useState(false);
  const [recurringHasPreviousPage, setRecurringHasPreviousPage] = useState(false);
  const [recurringLoading, setRecurringLoading] = useState(false);
  const [recurringError, setRecurringError] = useState<string | null>(null);
  const [recurringExporting, setRecurringExporting] = useState(false);
  const [recurringExportError, setRecurringExportError] = useState<string | null>(null);
  const recurringAbortRef = useRef<AbortController | null>(null);

  const recurringFilterParams = useCallback((f: typeof recurringFilters = recurringFilters): Record<string, unknown> => {
    const p: Record<string, unknown> = {};
    if (f.dateFrom) p.dateFrom = f.dateFrom;
    if (f.dateTo) p.dateTo = f.dateTo;
    if (f.departmentId) p.departmentId = +f.departmentId;
    if (f.status) p.status = f.status;
    if (f.recurrenceType) p.recurrenceType = f.recurrenceType;
    if (f.priority) p.priority = f.priority;
    if (f.groupBy) p.groupBy = f.groupBy;
    return p;
  }, [recurringFilters]);

  const loadRecurringObligations = useCallback(async (page: number, pageSize: number, overrideFilters?: typeof recurringFilters) => {
    recurringAbortRef.current?.abort();
    const controller = new AbortController();
    recurringAbortRef.current = controller;
    setRecurringLoading(true);
    setRecurringError(null);
    try {
      const params = recurringFilterParams(overrideFilters);
      const [summaryRes, detailsRes] = await Promise.all([
        reportsApi.recurringObligationsSummary(params, { signal: controller.signal }),
        reportsApi.recurringObligationsDetails({ ...params, page, pageSize }, { signal: controller.signal }),
      ]);
      if (controller.signal.aborted) return;
      setRecurringSummary(summaryRes.data);
      setRecurringRows(detailsRes.data.items);
      setRecurringPage(detailsRes.data.page);
      setRecurringPageSize(detailsRes.data.pageSize);
      setRecurringTotalCount(detailsRes.data.totalCount);
      setRecurringTotalPages(detailsRes.data.totalPages);
      setRecurringHasNextPage(detailsRes.data.hasNextPage);
      setRecurringHasPreviousPage(detailsRes.data.hasPreviousPage);
    } catch (err) {
      if (controller.signal.aborted || (isAxiosError(err) && err.code === 'ERR_CANCELED')) return;
      setRecurringSummary(null);
      setRecurringRows([]);
      setRecurringError('تعذر تحميل تقرير الالتزامات الدورية');
    } finally {
      if (!controller.signal.aborted) setRecurringLoading(false);
    }
  }, [recurringFilterParams]);

  useEffect(() => {
    void Promise.resolve().then(() => loadRecurringObligations(1, recurringPageSize));
    return () => {
      recurringAbortRef.current?.abort();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const applyRecurringFilters = () => loadRecurringObligations(1, recurringPageSize);

  const resetRecurringFilters = () => {
    const emptyFilters = { dateFrom: '', dateTo: '', departmentId: '', status: '', recurrenceType: '', priority: '', groupBy: '' };
    setRecurringFilters(emptyFilters);
    loadRecurringObligations(1, recurringPageSize, emptyFilters);
  };

  const changeRecurringPage = (page: number) => loadRecurringObligations(page, recurringPageSize);

  const changeRecurringPageSize = (pageSize: number) => loadRecurringObligations(1, pageSize);

  const exportRecurringExcel = async () => {
    if (recurringExporting) return;
    setRecurringExportError(null);
    setRecurringExporting(true);
    try {
      const res = await reportsApi.exportRecurringObligationsExcel(recurringFilterParams());
      downloadBlob(res.data, 'recurring-obligations.xlsx');
    } catch {
      setRecurringExportError('تعذر تصدير تقرير الالتزامات الدورية. حاول مرة أخرى.');
    } finally {
      setRecurringExporting(false);
    }
  };

  const [deptSnapshotFilters, setDeptSnapshotFilters] = useState({ dateFrom: '', dateTo: '', departmentId: '' });
  const [deptSnapshot, setDeptSnapshot] = useState<DepartmentObligationSnapshot | null>(null);
  const [deptSnapshotLoading, setDeptSnapshotLoading] = useState(false);
  const [deptSnapshotError, setDeptSnapshotError] = useState<string | null>(null);
  const [deptSnapshotExporting, setDeptSnapshotExporting] = useState(false);
  const [deptSnapshotExportError, setDeptSnapshotExportError] = useState<string | null>(null);
  const deptSnapshotAbortRef = useRef<AbortController | null>(null);

  const deptSnapshotFilterParams = useCallback((f: typeof deptSnapshotFilters = deptSnapshotFilters): Record<string, unknown> => {
    const p: Record<string, unknown> = {};
    if (f.dateFrom) p.dateFrom = f.dateFrom;
    if (f.dateTo) p.dateTo = f.dateTo;
    if (f.departmentId) p.departmentId = +f.departmentId;
    return p;
  }, [deptSnapshotFilters]);

  const loadDeptSnapshot = useCallback(async (overrideFilters?: typeof deptSnapshotFilters) => {
    deptSnapshotAbortRef.current?.abort();
    const controller = new AbortController();
    deptSnapshotAbortRef.current = controller;
    setDeptSnapshotLoading(true);
    setDeptSnapshotError(null);
    try {
      const res = await reportsApi.departmentObligationSnapshot(deptSnapshotFilterParams(overrideFilters), { signal: controller.signal });
      if (controller.signal.aborted) return;
      setDeptSnapshot(res.data);
    } catch (err) {
      if (controller.signal.aborted || (isAxiosError(err) && err.code === 'ERR_CANCELED')) return;
      setDeptSnapshot(null);
      setDeptSnapshotError('تعذر تحميل لقطة التزامات الإدارات');
    } finally {
      if (!controller.signal.aborted) setDeptSnapshotLoading(false);
    }
  }, [deptSnapshotFilterParams]);

  useEffect(() => {
    void Promise.resolve().then(() => loadDeptSnapshot());
    return () => {
      deptSnapshotAbortRef.current?.abort();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const applyDeptSnapshotFilters = () => loadDeptSnapshot();

  const resetDeptSnapshotFilters = () => {
    const emptyFilters = { dateFrom: '', dateTo: '', departmentId: '' };
    setDeptSnapshotFilters(emptyFilters);
    loadDeptSnapshot(emptyFilters);
  };

  const exportDeptSnapshotExcel = async () => {
    if (deptSnapshotExporting) return;
    setDeptSnapshotExportError(null);
    setDeptSnapshotExporting(true);
    try {
      const res = await reportsApi.exportDepartmentObligationSnapshotExcel(deptSnapshotFilterParams());
      downloadBlob(res.data, 'department-obligation-snapshot.xlsx');
    } catch {
      setDeptSnapshotExportError('تعذر تصدير لقطة التزامات الإدارات. حاول مرة أخرى.');
    } finally {
      setDeptSnapshotExporting(false);
    }
  };

  useEffect(() => {
    if (searchParams.get('tab')) return;
    setSearchParams((prev) => {
      const next = new URLSearchParams(prev);
      next.set('tab', 'open');
      return next;
    }, { replace: true });
  }, [searchParams, setSearchParams]);

  useEffect(() => {
    tabStatesRef.current = tabStates;
  }, [tabStates]);

  const filterParams = useCallback((): Record<string, unknown> => {
    const f = appliedFilters;
    const p: Record<string, unknown> = {};
    if (f.dateFrom) p.dateFrom = f.dateFrom;
    if (f.dateTo) p.dateTo = f.dateTo;
    if (f.categoryId) p.categoryId = +f.categoryId;
    if (f.departmentId) p.departmentId = +f.departmentId;
    if (f.status) p.status = f.status;
    if (f.incomingSourceType) p.incomingSourceType = f.incomingSourceType;
    if (debouncedSearch.trim()) p.search = debouncedSearch.trim();
    return p;
  }, [appliedFilters, debouncedSearch]);

  const filterKey = useMemo(() => JSON.stringify(filterParams()), [filterParams]);
  const summaryToken = `${filterKey}:${summaryRetryKey}`;
  const summaryLoading = summaryReadyToken !== summaryToken;
  const analyticsLoading = analyticsReadyKey !== filterKey;
  const analyticsLoaded = analyticsReadyKey === filterKey && analyticsHasData;

  const loadTabDetails = useCallback(async (
    tabKey: ReportTab,
    page: number,
    pageSize: number,
    force = false,
  ) => {
    const params = filterParams();
    const fetchKey = buildFetchKey(tabKey, page, pageSize, params);
    const prev = tabStatesRef.current[tabKey];

    if (!force && !prev.stale && prev.loaded && prev.fetchKey === fetchKey) return;

    abortRef.current?.abort();
    const controller = new AbortController();
    abortRef.current = controller;

    setTabStates((s) => ({
      ...s,
      [tabKey]: { ...s[tabKey], loading: true, error: null },
    }));

    const cfg = tabConfig.find((t) => t.key === tabKey)!;

    try {
      const res = await cfg.loader({ ...params, page, pageSize }, { signal: controller.signal });
      if (controller.signal.aborted) return;
      setTabStates((s) => ({
        ...s,
        [tabKey]: {
          items: res.data.items,
          page: res.data.page,
          pageSize: res.data.pageSize,
          totalCount: res.data.totalCount,
          totalPages: res.data.totalPages,
          hasNextPage: res.data.hasNextPage,
          hasPreviousPage: res.data.hasPreviousPage,
          loading: false,
          error: null,
          loaded: true,
          stale: false,
          fetchKey,
        },
      }));
    } catch (err) {
      if (controller.signal.aborted) return;
      const message = extractErrorMessage(err);
      setTabStates((s) => ({
        ...s,
        [tabKey]: {
          ...s[tabKey],
          loading: false,
          error: message || 'تعذر تحميل التقرير',
          loaded: true,
          stale: false,
        },
      }));
    }
  }, [filterParams]);

  useEffect(() => {
    categoriesApi.getAll().then((r) => setCategories(r.data));
    departmentsApi.getAll().then((r) => setDepartments(r.data));
  }, []);

  useEffect(() => {
    summaryAbortRef.current?.abort();
    const controller = new AbortController();
    summaryAbortRef.current = controller;
    const token = summaryToken;

    reportsApi.pageSummary(filterParams(), { signal: controller.signal })
      .then((res) => {
        if (!controller.signal.aborted) {
          setSectionCounts(res.data);
          setSummaryError(null);
        }
      })
      .catch((err) => {
        if (controller.signal.aborted || (isAxiosError(err) && err.code === 'ERR_CANCELED')) return;
        setSectionCounts(null);
        setSummaryError('تعذر تحميل ملخص التقارير');
      })
      .finally(() => {
        if (!controller.signal.aborted) setSummaryReadyToken(token);
      });

    return () => controller.abort();
  }, [filterKey, summaryRetryKey, filterParams, summaryToken]);

  const loadAnalyticsFetch = useCallback(async (targetKey: string) => {
    const requestId = ++analyticsRequestIdRef.current;
    const p = filterParams();
    try {
      const [cat, inc, out, dept] = await Promise.all([
        reportsApi.byCategory(p),
        reportsApi.byIncomingParty(p),
        reportsApi.byOutgoingDepartment(p),
        reportsApi.departmentSummary(p),
      ]);
      if (requestId !== analyticsRequestIdRef.current) return;
      setCategoryReport(cat.data as typeof categoryReport);
      setIncomingReport(inc.data as typeof incomingReport);
      setOutgoingDeptReport(out.data);
      setDeptSummary(dept.data);
      setAnalyticsHasData(true);
      setAnalyticsUpdatedAt(new Date());
      setAnalyticsError(null);
      setAnalyticsReadyKey(targetKey);
    } catch {
      if (requestId !== analyticsRequestIdRef.current) return;
      setAnalyticsHasData(false);
      setAnalyticsError('تعذر تحميل التحليلات. حاول مرة أخرى.');
      setAnalyticsReadyKey(targetKey);
    }
  }, [filterParams]);

  const loadAnalytics = useCallback(() => {
    setAnalyticsReadyKey(null);
    loadAnalyticsFetch(filterKey).catch(() => {
      setAnalyticsError('تعذر تحميل التحليلات. حاول مرة أخرى.');
    });
  }, [filterKey, loadAnalyticsFetch]);

  useEffect(() => {
    const el = monthlyRef.current;
    if (!el || monthlyLoaded) return;
    const observer = new IntersectionObserver((entries) => {
      if (entries.some((e) => e.isIntersecting)) {
        setMonthlyLoaded(true);
        observer.disconnect();
      }
    }, { rootMargin: '100px' });
    observer.observe(el);
    return () => observer.disconnect();
  }, [monthlyLoaded]);

  useEffect(() => {
    if (!monthlyLoaded) return;
    reportsApi.monthly(year).then((r) => setMonthly(r.data as typeof monthly)).catch(() => {});
  }, [year, monthlyLoaded]);

  useEffect(() => {
    void Promise.resolve()
      .then(() => loadAnalyticsFetch(filterKey))
      .catch(() => {
        setAnalyticsError('تعذر تحميل التحليلات. حاول مرة أخرى.');
      });
  }, [filterKey, loadAnalyticsFetch]);

  useEffect(() => {
    loadTabDetails(tab, 1, tabStatesRef.current[tab].pageSize, true);
  }, [filterKey, tab, loadTabDetails]);

  const resetFilters = () => {
    const empty = {
      dateFrom: '', dateTo: '', categoryId: '', departmentId: '', status: '', incomingSourceType: '', search: '',
    };
    setDraftFilters(empty);
    setTabStates((prev) => {
      const next = { ...prev };
      for (const t of tabConfig) {
        next[t.key] = { ...prev[t.key], page: 1, stale: true };
      }
      return next;
    });
  };

  const selectTab = (tabKey: ReportTab) => {
    setSearchParams((prev) => {
      const next = new URLSearchParams(prev);
      next.set('tab', tabKey);
      return next;
    });
    const state = tabStates[tabKey];
    if (state.stale || !state.loaded) {
      loadTabDetails(tabKey, state.page, state.pageSize);
    }
  };

  const updateTabPage = (tabKey: ReportTab, page: number) => {
    const state = tabStates[tabKey];
    loadTabDetails(tabKey, page, state.pageSize, true);
  };

  const updateTabPageSize = (tabKey: ReportTab, pageSize: number) => {
    setTabStates((s) => ({
      ...s,
      [tabKey]: { ...s[tabKey], page: 1, pageSize, stale: true },
    }));
    loadTabDetails(tabKey, 1, pageSize, true);
  };

  const exportExcel = async (currentPageOnly: boolean) => {
    const state = tabStates[tab];
    if (state.loading || exporting) return;
    setExportError(null);
    setExporting(true);
    try {
      const params: Record<string, unknown> = {
        ...filterParams(),
        currentPageOnly,
        page: state.page,
        pageSize: state.pageSize,
      };
      const res = await reportsApi.exportExcel(tab, params);
      downloadBlob(res.data, `report-${tab}${currentPageOnly ? '-page' : '-all'}.xlsx`);
    } catch {
      setExportError('تعذر تصدير التقرير. حاول مرة أخرى.');
    } finally {
      setExporting(false);
    }
  };

  const exportDepartmentReport = async (format: 'excel' | 'pdf') => {
    if (analyticsLoading || exporting) return;
    setExportError(null);
    setExporting(true);
    try {
      const params = filterParams();
      if (format === 'excel') {
        const res = await reportsApi.exportDepartmentIncomingClosedExcel(params);
        downloadBlob(res.data, 'department-incoming-closed.xlsx');
      } else {
        const res = await reportsApi.exportDepartmentIncomingClosedPdf(params);
        downloadBlob(res.data, 'department-incoming-closed.pdf');
      }
    } catch {
      setExportError('تعذر تصدير التحليلات. حاول مرة أخرى.');
    } finally {
      setExporting(false);
    }
  };

  const currentState = tabStates[tab];
  const showTimingColumns = TIMING_REPORT_TABS.includes(tab);
  const tableColSpan = showTimingColumns ? 11 : 8;
  const analyticsViewState = getAnalyticsViewState(analyticsLoading, analyticsLoaded);
  const analyticsStatusText = getAnalyticsStatusText(analyticsLoading, analyticsUpdatedAt, analyticsLoaded);
  const monthNames = ['يناير', 'فبراير', 'مارس', 'أبريل', 'مايو', 'يونيو', 'يوليو', 'أغسطس', 'سبتمبر', 'أكتوبر', 'نوفمبر', 'ديسمبر'];

  return (
    <div>
      <PageHeader
        title="التقارير"
        actions={(
          <div className="btn-group">
            <button
              type="button"
              className="btn btn-outline"
              onClick={() => exportExcel(true)}
              disabled={currentState.loading || exporting}
            >
              تصدير الصفحة الحالية
            </button>
            <button
              type="button"
              className="btn btn-primary"
              onClick={() => exportExcel(false)}
              disabled={currentState.loading || exporting}
            >
              تصدير جميع النتائج
            </button>
          </div>
        )}
      />

      <div className="card filter-card mb-4">
        <div className="filter-form">
          <label>من تاريخ</label>
          <input type="date" value={draftFilters.dateFrom} onChange={(e) => setDraftFilters({ ...draftFilters, dateFrom: e.target.value })} />
          <label>إلى تاريخ</label>
          <input type="date" value={draftFilters.dateTo} onChange={(e) => setDraftFilters({ ...draftFilters, dateTo: e.target.value })} />
          <input placeholder="بحث (رقم وارد، تتبع، موضوع)" value={draftFilters.search} onChange={(e) => setDraftFilters({ ...draftFilters, search: e.target.value })} />
          <select value={draftFilters.incomingSourceType} onChange={(e) => setDraftFilters({ ...draftFilters, incomingSourceType: e.target.value })}>
            <option value="">نوع الوارد: الكل</option>
            <option value="External">خارجي</option>
            <option value="Internal">داخلي</option>
          </select>
          <select value={draftFilters.categoryId} onChange={(e) => setDraftFilters({ ...draftFilters, categoryId: e.target.value })}>
            <option value="">كل التصنيفات</option>
            {categories.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
          </select>
          <select value={draftFilters.departmentId} onChange={(e) => setDraftFilters({ ...draftFilters, departmentId: e.target.value })}>
            <option value="">كل الإدارات</option>
            {departments.map((d) => <option key={d.id} value={d.id}>{d.name}</option>)}
          </select>
          <button type="button" className="btn btn-outline" onClick={resetFilters}>إعادة تعيين الفلاتر</button>
          <button type="button" className="btn btn-secondary" onClick={() => loadTabDetails(tab, tabStates[tab].page, tabStates[tab].pageSize, true)}>
            تحديث التقرير
          </button>
        </div>
      </div>

      {(appliedFilters.dateFrom || appliedFilters.dateTo || appliedFilters.categoryId || appliedFilters.departmentId || appliedFilters.incomingSourceType || debouncedSearch.trim()) && (
        <output className="text-muted mb-2" aria-live="polite">
          فلاتر مطبقة:
          {appliedFilters.dateFrom && ` من ${appliedFilters.dateFrom}`}
          {appliedFilters.dateTo && ` إلى ${appliedFilters.dateTo}`}
          {appliedFilters.categoryId && ` | تصنيف: ${categories.find((c) => String(c.id) === appliedFilters.categoryId)?.name ?? appliedFilters.categoryId}`}
          {appliedFilters.departmentId && ` | إدارة: ${departments.find((d) => String(d.id) === appliedFilters.departmentId)?.name ?? appliedFilters.departmentId}`}
          {appliedFilters.incomingSourceType && ` | نوع الوارد: ${appliedFilters.incomingSourceType === 'Internal' ? 'داخلي' : 'خارجي'}`}
          {debouncedSearch.trim() && ` | بحث: ${debouncedSearch.trim()}`}
        </output>
      )}

      {exportError && <div className="alert alert-error">{exportError}</div>}

      {summaryError && (
        <div className="alert alert-error">
          {summaryError}
          <button
            type="button"
            className="btn btn-sm btn-outline ms-2"
            onClick={() => setSummaryRetryKey((k) => k + 1)}
            disabled={summaryLoading}
          >
            إعادة المحاولة
          </button>
        </div>
      )}

      <div className="tabs">
        {tabConfig.map((t) => (
          <button
            key={t.key}
            className={tab === t.key ? 'active' : ''}
            onClick={() => selectTab(t.key)}
          >
            {t.label}
            <span className="tab-count">
              {summaryLoading ? '…' : (sectionCounts?.[t.countKey] ?? 0)}
            </span>
          </button>
        ))}
      </div>

      {currentState?.error && <div className="alert alert-error">{currentState.error}</div>}

      {currentState && (
        <>
          <table className="data-table">
            <thead>
              <tr>
                <th>رقم الوارد</th>
                <th>الموضوع</th>
                <th>الجهة الوارد منها</th>
                <th>التصنيف</th>
                <th>الإدارة</th>
                <th>الحالة</th>
                {showTimingColumns && (
                  <>
                    <th>تاريخ الرد المطلوب</th>
                    <th>حالة الرد</th>
                    <th>آخر تعقيب</th>
                  </>
                )}
                <th>التاريخ</th>
                <th>عرض</th>
              </tr>
            </thead>
            <tbody>
              {currentState.loading && <TableSkeleton rows={currentState.pageSize} />}
              {!currentState.loading && currentState.items.map((t) => (
                <tr key={t.id} className={t.isOverdue ? 'row-overdue' : ''}>
                  <td>{t.incomingNumber}</td>
                  <td>{t.subject}</td>
                  <td>{t.incomingFromDisplayName || '-'}</td>
                  <td>{t.categoryName || '-'}</td>
                  <td><DepartmentBadges names={t.outgoingDepartmentsDisplayNames} /></td>
                  <td>
                    <span className={`badge ${statusBadgeClass(t.status, t.isOverdue)}`}>
                      {statusLabels[t.status] || t.status}
                    </span>
                  </td>
                  {showTimingColumns && (
                    <>
                      <td>{t.responseDueDate ? <DateDisplay date={t.responseDueDate} /> : '—'}</td>
                      <td>
                        <span className={`badge ${responseTimingBadgeClass(t.responseTimingStatus)}`}>
                          {t.responseTimingLabel || '—'}
                        </span>
                      </td>
                      <td>{t.lastFollowUpDate ? <DateDisplay date={t.lastFollowUpDate} /> : '—'}</td>
                    </>
                  )}
                  <td><DateDisplay date={t.incomingDate} /></td>
                  <td><Link to={`/transactions/${t.id}`} className="btn btn-sm">عرض</Link></td>
                </tr>
              ))}
              {!currentState.loading && currentState.loaded && currentState.items.length === 0 && !currentState.error && (
                <tr><td colSpan={tableColSpan} className="text-center">لا توجد معاملات مطابقة للفلاتر المحددة.</td></tr>
              )}
            </tbody>
          </table>

          {currentState.loaded && (
            <div className="pagination">
              <span>إجمالي النتائج: {currentState.totalCount}</span>
              <span>صفحة {currentState.page} من {currentState.totalPages || 1}</span>
              <select
                value={currentState.pageSize}
                onChange={(e) => updateTabPageSize(tab, +e.target.value)}
                aria-label="عدد النتائج في الصفحة"
              >
                {PAGE_SIZE_OPTIONS.map((n) => <option key={n} value={n}>{n} / صفحة</option>)}
              </select>
              <button disabled={!currentState.hasPreviousPage || currentState.loading} onClick={() => updateTabPage(tab, currentState.page - 1)}>السابق</button>
              <button disabled={!currentState.hasNextPage || currentState.loading} onClick={() => updateTabPage(tab, currentState.page + 1)}>التالي</button>
            </div>
          )}
        </>
      )}

      <div className="card mt-4">
        <div className="card-header">
          <h3 className="card-title">التحليلات والتقارير التفصيلية</h3>
          <div className="btn-group">
            <button
              type="button"
              className="btn btn-secondary"
              onClick={loadAnalytics}
              disabled={analyticsLoading || exporting}
            >
              {analyticsLoading ? 'جاري التحديث...' : 'تحديث التحليلات'}
            </button>
            <button
              type="button"
              className="btn btn-outline"
              disabled={analyticsLoading || exporting}
              onClick={() => exportDepartmentReport('excel')}
            >
              تصدير Excel
            </button>
            <button
              type="button"
              className="btn btn-outline"
              disabled={analyticsLoading || exporting}
              onClick={() => exportDepartmentReport('pdf')}
            >
              تصدير PDF
            </button>
          </div>
        </div>
        <output className="text-muted mb-2" aria-live="polite">
          {analyticsStatusText}
        </output>
        <p className="text-muted mb-2">يُحسب الوارد حسب تاريخ الوارد (الإدارات الصادر لها)، والمغلق حسب تاريخ الإغلاق ضمن الفترة المحددة. تتحدث التحليلات تلقائيًا عند تغيير الفلاتر.</p>
        {analyticsError && <div className="alert alert-error mb-2">{analyticsError}</div>}
        {analyticsViewState === 'loading-initial' && <div className="loading">جاري تحميل التحليلات...</div>}
        {analyticsViewState === 'loading-refresh' && <div className="loading">جاري التحديث...</div>}
        {analyticsViewState === 'empty' && (
          <div className="text-center text-muted" style={{ padding: '1.5rem' }}>لا توجد بيانات تحليلات للفلاتر الحالية.</div>
        )}
        {analyticsViewState === 'content' && (
          <>
          <h4 className="mb-2">تقرير الوارد والمغلق لكل إدارة</h4>
          <table className="data-table">
            <thead>
              <tr>
                <th>الإدارة</th><th>إجمالي الوارد</th><th>مفتوح</th><th>بانتظار رد</th>
                <th>متأخر</th><th>مغلق</th><th>نسبة الإغلاق</th>
              </tr>
            </thead>
            <tbody>
              {deptSummary.map((d) => (
                <tr key={d.departmentId}>
                  <td>{d.departmentName}</td>
                  <td>{d.totalIncoming}</td>
                  <td>{d.openCount}</td>
                  <td>{d.waitingForReplyCount}</td>
                  <td>{d.overdueCount}</td>
                  <td>{d.closedCount}</td>
                  <td>{d.closeRate}%</td>
                </tr>
              ))}
              {deptSummary.length === 0 && <tr><td colSpan={7} className="text-center">لا توجد بيانات</td></tr>}
            </tbody>
          </table>

      <div className="dashboard-grid mt-4">
        <div className="card">
          <div className="card-header"><h3 className="card-title">حسب التصنيف</h3></div>
          <table className="data-table">
            <thead><tr><th>التصنيف</th><th>العدد</th></tr></thead>
            <tbody>{categoryReport.map((c, i) => <tr key={i}><td>{c.categoryName}</td><td>{c.count}</td></tr>)}</tbody>
          </table>
        </div>
        <div className="card">
          <div className="card-header"><h3 className="card-title">حسب الجهات الوارد منها</h3></div>
          <table className="data-table">
            <thead><tr><th>الجهة</th><th>العدد</th></tr></thead>
            <tbody>{incomingReport.map((p, i) => <tr key={i}><td>{p.partyName}</td><td>{p.transactionCount}</td></tr>)}</tbody>
          </table>
        </div>
        <div className="card">
          <div className="card-header"><h3 className="card-title">حسب الإدارات الصادر لها</h3></div>
          <table className="data-table">
            <thead>
              <tr><th>الإدارة</th><th>العدد</th><th>مفتوح</th><th>مغلق</th><th>متأخر</th></tr>
            </thead>
            <tbody>
              {outgoingDeptReport.map((d) => (
                <tr key={d.departmentId}>
                  <td>{d.departmentName}</td>
                  <td>{d.transactionCount}</td>
                  <td>{d.openCount}</td>
                  <td>{d.closedCount}</td>
                  <td>{d.overdueCount}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
          </>
        )}
      </div>

      <div className="card mt-4" ref={monthlyRef}>
        <div className="card-header"><h3 className="card-title">التقرير الشهري للوارد والصادر</h3></div>
        <select value={year} onChange={(e) => setYear(+e.target.value)} className="mb-2">
          {[2024, 2025, 2026, 2027].map((y) => <option key={y} value={y}>{y}</option>)}
        </select>
        {!monthlyLoaded ? (
          <div className="loading">سيُحمّل عند التمرير...</div>
        ) : (
          <table className="data-table">
            <thead><tr><th>الشهر</th><th>الوارد</th><th>الصادر</th></tr></thead>
            <tbody>
              {monthly.map((m) => (
                <tr key={m.month}><td>{monthNames[m.month - 1]}</td><td>{m.incomingCount}</td><td>{m.outgoingCount}</td></tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      <div className="card mt-4">
        <div className="card-header">
          <h3 className="card-title">تقرير الالتزامات الدورية</h3>
          <div className="btn-group">
            <button
              type="button"
              className="btn btn-secondary"
              onClick={applyRecurringFilters}
              disabled={recurringLoading}
            >
              تحديث تقرير الالتزامات الدورية
            </button>
            <button
              type="button"
              className="btn btn-outline"
              onClick={exportRecurringExcel}
              disabled={recurringLoading || recurringExporting}
            >
              تصدير الالتزامات الدورية Excel
            </button>
          </div>
        </div>

        <div className="filter-form mb-2">
          <label htmlFor="recurring-obligations-date-from">من تاريخ</label>
          <input id="recurring-obligations-date-from" type="date" value={recurringFilters.dateFrom} onChange={(e) => setRecurringFilters({ ...recurringFilters, dateFrom: e.target.value })} />
          <label htmlFor="recurring-obligations-date-to">إلى تاريخ</label>
          <input id="recurring-obligations-date-to" type="date" value={recurringFilters.dateTo} onChange={(e) => setRecurringFilters({ ...recurringFilters, dateTo: e.target.value })} />
          <select value={recurringFilters.departmentId} onChange={(e) => setRecurringFilters({ ...recurringFilters, departmentId: e.target.value })}>
            <option value="">كل الإدارات</option>
            {departments.map((d) => <option key={d.id} value={d.id}>{d.name}</option>)}
          </select>
          <select value={recurringFilters.status} onChange={(e) => setRecurringFilters({ ...recurringFilters, status: e.target.value })}>
            <option value="">كل الحالات</option>
            <option value="Active">نشط</option>
            <option value="Paused">موقوف</option>
            <option value="Terminated">منتهٍ</option>
          </select>
          <select value={recurringFilters.recurrenceType} onChange={(e) => setRecurringFilters({ ...recurringFilters, recurrenceType: e.target.value })}>
            <option value="">كل أنواع التكرار</option>
            <option value="Monthly">شهري</option>
            <option value="Quarterly">ربع سنوي</option>
            <option value="SemiAnnual">نصف سنوي</option>
            <option value="Annual">سنوي</option>
          </select>
          <select value={recurringFilters.priority} onChange={(e) => setRecurringFilters({ ...recurringFilters, priority: e.target.value })}>
            <option value="">كل الأولويات</option>
            <option value="Normal">عادي</option>
            <option value="Urgent">عاجل</option>
            <option value="VeryUrgent">عاجل جداً</option>
          </select>
          <select value={recurringFilters.groupBy} onChange={(e) => setRecurringFilters({ ...recurringFilters, groupBy: e.target.value })}>
            <option value="">بدون تجميع</option>
            <option value="department">تجميع حسب الإدارة</option>
            <option value="status">تجميع حسب الحالة</option>
            <option value="recurrenceType">تجميع حسب نوع التكرار</option>
          </select>
          <button type="button" className="btn btn-outline" onClick={resetRecurringFilters}>إعادة تعيين فلاتر الالتزامات الدورية</button>
        </div>

        {recurringExportError && <div className="alert alert-error">{recurringExportError}</div>}
        {recurringError && <div className="alert alert-error">{recurringError}</div>}

        {recurringSummary && (
          <div className="dashboard-grid mb-3">
            <div className="card"><strong>الإجمالي</strong><div>{recurringSummary.total}</div></div>
            <div className="card"><strong>نشط</strong><div>{recurringSummary.active}</div></div>
            <div className="card"><strong>قادم</strong><div>{recurringSummary.upcoming}</div></div>
            <div className="card"><strong>قريب الاستحقاق</strong><div>{recurringSummary.dueSoon}</div></div>
            <div className="card"><strong>متأخر</strong><div>{recurringSummary.overdue}</div></div>
            <div className="card"><strong>موقوف</strong><div>{recurringSummary.suspended}</div></div>
            <div className="card"><strong>منتهٍ</strong><div>{recurringSummary.terminated}</div></div>
          </div>
        )}

        {recurringSummary && recurringSummary.groups.length > 0 && (
          <table className="data-table mb-3">
            <thead><tr><th>المجموعة</th><th>العدد</th></tr></thead>
            <tbody>
              {recurringSummary.groups.map((g) => (
                <tr key={g.groupKey}><td>{g.groupLabel}</td><td>{g.count}</td></tr>
              ))}
            </tbody>
          </table>
        )}

        <table className="data-table">
          <thead>
            <tr>
              <th>العنوان</th>
              <th>الإدارة المالكة</th>
              <th>الإدارات المسؤولة</th>
              <th>نوع التكرار</th>
              <th>تاريخ البدء</th>
              <th>الاستحقاق القادم</th>
              <th>آخر إتمام</th>
              <th>الحالة</th>
              <th>حالة الجدولة</th>
              <th>الأيام المتبقية/المتأخرة</th>
              <th>الأولوية</th>
            </tr>
          </thead>
          <tbody>
            {recurringLoading && <TableSkeleton rows={recurringPageSize} columns={11} />}
            {!recurringLoading && recurringRows.map((r) => (
              <tr key={r.templateId} className={r.scheduleStatus === 'Overdue' ? 'row-overdue' : ''}>
                <td>{r.title}</td>
                <td>{r.owningDepartmentName || '-'}</td>
                <td><DepartmentBadges names={r.responsibleDepartmentNames} /></td>
                <td>{recurrenceTypeLabels[r.recurrenceType] || r.recurrenceType}</td>
                <td><DateDisplay date={r.startDate} /></td>
                <td>{r.nextDueDate ? <DateDisplay date={r.nextDueDate} /> : '—'}</td>
                <td>{r.lastCompletionDate ? <DateDisplay date={r.lastCompletionDate} /> : '—'}</td>
                <td>
                  <span className="badge">{recurringTemplateStatusLabels[r.status] || r.status}</span>
                </td>
                <td>
                  <span className={`badge ${recurringScheduleStatusBadgeClass(r.scheduleStatus)}`}>
                    {recurringScheduleStatusLabels[r.scheduleStatus] || r.scheduleStatus}
                  </span>
                </td>
                <td>{r.daysRemaining ?? '—'}</td>
                <td>{priorityLabels[r.priority] || r.priority}</td>
              </tr>
            ))}
            {!recurringLoading && recurringRows.length === 0 && !recurringError && (
              <tr><td colSpan={11} className="text-center">لا توجد التزامات دورية مطابقة للفلاتر المحددة.</td></tr>
            )}
          </tbody>
        </table>

        <div className="pagination">
          <span>إجمالي النتائج: {recurringTotalCount}</span>
          <span>صفحة {recurringPage} من {recurringTotalPages || 1}</span>
          <select
            value={recurringPageSize}
            onChange={(e) => changeRecurringPageSize(+e.target.value)}
            aria-label="عدد النتائج في الصفحة"
          >
            {PAGE_SIZE_OPTIONS.map((n) => <option key={n} value={n}>{n} / صفحة</option>)}
          </select>
          <button disabled={!recurringHasPreviousPage || recurringLoading} onClick={() => changeRecurringPage(recurringPage - 1)}>السابق</button>
          <button disabled={!recurringHasNextPage || recurringLoading} onClick={() => changeRecurringPage(recurringPage + 1)}>التالي</button>
        </div>
      </div>

      <div className="card mt-4">
        <div className="card-header">
          <h3 className="card-title">لقطة التزامات الإدارات</h3>
          <div className="btn-group">
            <button
              type="button"
              className="btn btn-secondary"
              onClick={() => loadDeptSnapshot()}
              disabled={deptSnapshotLoading}
            >
              تحديث لقطة التزامات الإدارات
            </button>
            <button
              type="button"
              className="btn btn-outline"
              onClick={exportDeptSnapshotExcel}
              disabled={deptSnapshotLoading || deptSnapshotExporting}
            >
              تصدير لقطة التزامات الإدارات Excel
            </button>
          </div>
        </div>

        <p className="text-muted mb-2">
          يفصل هذا الجدول بين إدارة المعاملة (المالكة) وبين الإدارات المسؤولة/المحالة إليها، حتى لا يُحمَّل الأداء على الإدارة المالكة وحدها عند وجود تأخير من إدارة أخرى.
        </p>

        <div className="filter-form mb-2">
          <label htmlFor="dept-snapshot-date-from">من تاريخ</label>
          <input id="dept-snapshot-date-from" type="date" value={deptSnapshotFilters.dateFrom} onChange={(e) => setDeptSnapshotFilters({ ...deptSnapshotFilters, dateFrom: e.target.value })} />
          <label htmlFor="dept-snapshot-date-to">إلى تاريخ</label>
          <input id="dept-snapshot-date-to" type="date" value={deptSnapshotFilters.dateTo} onChange={(e) => setDeptSnapshotFilters({ ...deptSnapshotFilters, dateTo: e.target.value })} />
          <select value={deptSnapshotFilters.departmentId} onChange={(e) => setDeptSnapshotFilters({ ...deptSnapshotFilters, departmentId: e.target.value })}>
            <option value="">كل الإدارات</option>
            {departments.map((d) => <option key={d.id} value={d.id}>{d.name}</option>)}
          </select>
          <button type="button" className="btn btn-outline" onClick={applyDeptSnapshotFilters}>تطبيق</button>
          <button type="button" className="btn btn-outline" onClick={resetDeptSnapshotFilters}>إعادة تعيين فلاتر لقطة الإدارات</button>
        </div>

        {deptSnapshotExportError && <div className="alert alert-error">{deptSnapshotExportError}</div>}
        {deptSnapshotError && <div className="alert alert-error">{deptSnapshotError}</div>}

        {deptSnapshot && (
          <div className="dashboard-grid mb-3">
            <div className="card"><strong>عدد الإدارات المشمولة</strong><div>{deptSnapshot.totalDepartmentsInScope}</div></div>
            <div className="card"><strong>إجمالي المعاملات المشمولة</strong><div>{deptSnapshot.totalDistinctObligations}</div></div>
            <div className="card"><strong>معاملات متعددة الإدارات</strong><div>{deptSnapshot.multiDepartmentObligationsCount}</div></div>
          </div>
        )}

        <div style={{ overflowX: 'auto' }}>
          <table className="data-table">
            <thead>
              <tr>
                <th>الإدارة</th>
                <th>مملوكة</th>
                <th>مسؤولة عنها</th>
                <th>محالة إليها</th>
                <th>إجراء مفتوح</th>
                <th>بانتظار الإجراء</th>
                <th>إجراء مكتمل</th>
                <th>إفادات مقدَّمة</th>
                <th>إفادات معتمدة</th>
                <th>متأخرة</th>
                <th>قريبة الاستحقاق</th>
                <th>متوسط أيام الفتح</th>
                <th>تعارض في التوثيق</th>
                <th>نوع المشاركة</th>
              </tr>
            </thead>
            <tbody>
              {deptSnapshotLoading && <TableSkeleton rows={5} columns={14} />}
              {!deptSnapshotLoading && deptSnapshot?.departments.map((d) => (
                <tr key={d.departmentId}>
                  <td>{d.departmentName}</td>
                  <td>{d.ownedCount}</td>
                  <td>{d.responsibleCount}</td>
                  <td>{d.referredCount}</td>
                  <td>{d.openActionCount}</td>
                  <td>{d.pendingActionCount}</td>
                  <td>{d.completedActionCount}</td>
                  <td>{d.submittedResponseCount}</td>
                  <td>{d.approvedResponseCount}</td>
                  <td>{d.overdueCount}</td>
                  <td>{d.dueSoonCount}</td>
                  <td>{d.averageDaysOpenAction ?? '—'}</td>
                  <td>{d.attributionMismatchCount}</td>
                  <td>{involvementCategoryLabels[d.involvementCategory] || d.involvementCategory}</td>
                </tr>
              ))}
              {!deptSnapshotLoading && deptSnapshot?.departments.length === 0 && !deptSnapshotError && (
                <tr><td colSpan={14} className="text-center">لا توجد بيانات مطابقة للفلاتر المحددة.</td></tr>
              )}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}
