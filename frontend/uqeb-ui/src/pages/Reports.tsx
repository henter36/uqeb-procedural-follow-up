import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { isAxiosError } from 'axios';
import { reportsApi, categoriesApi, departmentsApi } from '../api/services';
import type {
  ReportTransactionRow, Category, Department, DepartmentSummaryReport,
  OutgoingDepartmentReport, ReportSectionCounts, PagedResult,
} from '../api/types';
import { statusLabels, statusBadgeClass } from '../utils/labels';
import DateDisplay from '../components/DateDisplay';
import DepartmentBadges from '../components/DepartmentBadges';

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
  { key: 'pending-assignments', label: 'تحويلات مفتوحة', countKey: 'openAssignments', loader: reportsApi.openAssignmentsDetails },
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

function parseReportTab(value: string | null): ReportTab | null {
  if (!value) return null;
  return tabConfig.some((t) => t.key === value) ? (value as ReportTab) : null;
}

function TableSkeleton({ rows = 5 }: { rows?: number }) {
  return (
    <>
      {Array.from({ length: rows }).map((_, i) => (
        <tr key={i} className="skeleton-row">
          <td><div className="skeleton-bar w-40" /></td>
          <td><div className="skeleton-bar w-80" /></td>
          <td><div className="skeleton-bar w-60" /></td>
          <td><div className="skeleton-bar w-40" /></td>
          <td><div className="skeleton-bar w-60" /></td>
          <td><div className="skeleton-bar w-40" /></td>
          <td><div className="skeleton-bar w-40" /></td>
          <td><div className="skeleton-bar w-40" /></td>
        </tr>
      ))}
    </>
  );
}

export default function ReportsPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const [tab, setTab] = useState<ReportTab | null>(() => parseReportTab(searchParams.get('tab')));
  const [tabStates, setTabStates] = useState<Record<ReportTab, TabState>>(() =>
    Object.fromEntries(tabConfig.map((t) => [t.key, defaultTabState()])) as Record<ReportTab, TabState>
  );
  const [categories, setCategories] = useState<Category[]>([]);
  const [departments, setDepartments] = useState<Department[]>([]);
  const [sectionCounts, setSectionCounts] = useState<ReportSectionCounts | null>(null);
  const [summaryLoading, setSummaryLoading] = useState(true);
  const [summaryError, setSummaryError] = useState<string | null>(null);
  const [summaryRetryKey, setSummaryRetryKey] = useState(0);
  const [categoryReport, setCategoryReport] = useState<{ categoryName: string; count: number }[]>([]);
  const [incomingReport, setIncomingReport] = useState<{ partyName: string; transactionCount: number }[]>([]);
  const [outgoingDeptReport, setOutgoingDeptReport] = useState<OutgoingDepartmentReport[]>([]);
  const [deptSummary, setDeptSummary] = useState<DepartmentSummaryReport[]>([]);
  const [analyticsError, setAnalyticsError] = useState<string | null>(null);
  const [analyticsLoading, setAnalyticsLoading] = useState(false);
  const [analyticsLoaded, setAnalyticsLoaded] = useState(false);
  const [monthly, setMonthly] = useState<{ month: number; incomingCount: number; outgoingCount: number }[]>([]);
  const [monthlyLoaded, setMonthlyLoaded] = useState(false);
  const [year, setYear] = useState(new Date().getFullYear());
  const [draftFilters, setDraftFilters] = useState({
    dateFrom: '', dateTo: '', categoryId: '', departmentId: '', status: '', incomingSourceType: '', search: '',
  });
  const [appliedFilters, setAppliedFilters] = useState({
    dateFrom: '', dateTo: '', categoryId: '', departmentId: '', status: '', incomingSourceType: '',
  });
  const [debouncedSearch, setDebouncedSearch] = useState('');
  const abortRef = useRef<AbortController | null>(null);
  const summaryAbortRef = useRef<AbortController | null>(null);
  const monthlyRef = useRef<HTMLDivElement>(null);
  const tabStatesRef = useRef(tabStates);
  tabStatesRef.current = tabStates;
  const initialUrlTabRef = useRef(parseReportTab(searchParams.get('tab')));

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
    if (!initialUrlTabRef.current) return;
    const urlTab = initialUrlTabRef.current;
    initialUrlTabRef.current = null;
    loadTabDetails(urlTab, 1, DEFAULT_PAGE_SIZE);
  }, [loadTabDetails]);

  useEffect(() => {
    const urlTab = parseReportTab(searchParams.get('tab'));
    if (!urlTab || urlTab === tab) return;
    setTab(urlTab);
    const state = tabStatesRef.current[urlTab];
    loadTabDetails(urlTab, state.page, state.pageSize);
  }, [searchParams, tab, loadTabDetails]);

  useEffect(() => {
    categoriesApi.getAll().then((r) => setCategories(r.data));
    departmentsApi.getAll().then((r) => setDepartments(r.data));
  }, []);

  useEffect(() => {
    summaryAbortRef.current?.abort();
    const controller = new AbortController();
    summaryAbortRef.current = controller;

    setSummaryLoading(true);
    setSummaryError(null);
    reportsApi.pageSummary(filterParams(), { signal: controller.signal })
      .then((res) => {
        if (!controller.signal.aborted) setSectionCounts(res.data);
      })
      .catch((err) => {
        if (controller.signal.aborted || (isAxiosError(err) && err.code === 'ERR_CANCELED')) return;
        setSectionCounts(null);
        setSummaryError('تعذر تحميل ملخص التقارير');
      })
      .finally(() => {
        if (!controller.signal.aborted) setSummaryLoading(false);
      });

    return () => controller.abort();
  }, [filterKey, summaryRetryKey]);

  useEffect(() => {
    const timer = window.setTimeout(() => setDebouncedSearch(draftFilters.search), 300);
    return () => window.clearTimeout(timer);
  }, [draftFilters.search]);

  const prevDebouncedSearch = useRef(debouncedSearch);
  useEffect(() => {
    if (prevDebouncedSearch.current === debouncedSearch) return;
    prevDebouncedSearch.current = debouncedSearch;
    setTabStates((prev) => {
      const next = { ...prev };
      for (const t of tabConfig) {
        next[t.key] = { ...prev[t.key], page: 1, stale: true };
      }
      return next;
    });
  }, [debouncedSearch]);

  const analyticsLoadedRef = useRef(false);
  const analyticsLoadingRef = useRef(false);
  analyticsLoadedRef.current = analyticsLoaded;

  const loadAnalytics = useCallback((force = false) => {
    if (analyticsLoadingRef.current) return;
    if (!force && analyticsLoadedRef.current) return;
    analyticsLoadingRef.current = true;
    setAnalyticsLoading(true);
    setAnalyticsError(null);
    const p = filterParams();
    Promise.all([
      reportsApi.byCategory(p),
      reportsApi.byIncomingParty(p),
      reportsApi.byOutgoingDepartment(p),
      reportsApi.departmentSummary(p),
    ])
      .then(([cat, inc, out, dept]) => {
        setCategoryReport(cat.data as typeof categoryReport);
        setIncomingReport(inc.data as typeof incomingReport);
        setOutgoingDeptReport(out.data);
        setDeptSummary(dept.data);
        analyticsLoadedRef.current = true;
        setAnalyticsLoaded(true);
        setAnalyticsError(null);
      })
      .catch(() => {
        setAnalyticsError('تعذر تحميل التحليلات. حاول مرة أخرى.');
      })
      .finally(() => {
        analyticsLoadingRef.current = false;
        setAnalyticsLoading(false);
      });
  }, [filterParams]);

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

  const resetAnalytics = useCallback(() => {
    analyticsLoadedRef.current = false;
    setAnalyticsLoaded(false);
    setCategoryReport([]);
    setIncomingReport([]);
    setOutgoingDeptReport([]);
    setDeptSummary([]);
    setAnalyticsError(null);
  }, []);

  const applyFilters = () => {
    setAppliedFilters({
      dateFrom: draftFilters.dateFrom,
      dateTo: draftFilters.dateTo,
      categoryId: draftFilters.categoryId,
      departmentId: draftFilters.departmentId,
      status: draftFilters.status,
      incomingSourceType: draftFilters.incomingSourceType,
    });
    setDebouncedSearch(draftFilters.search);
    setTabStates((prev) => {
      const next = { ...prev };
      for (const t of tabConfig) {
        next[t.key] = { ...prev[t.key], page: 1, stale: true };
      }
      return next;
    });
    const wasAnalyticsLoaded = analyticsLoadedRef.current;
    resetAnalytics();
    if (wasAnalyticsLoaded) loadAnalytics(true);
    if (tab) loadTabDetails(tab, 1, tabStates[tab].pageSize, true);
  };

  const selectTab = (tabKey: ReportTab) => {
    setTab(tabKey);
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
    if (!tab) return;
    const state = tabStates[tab];
    const params: Record<string, unknown> = {
      ...filterParams(),
      currentPageOnly,
      page: state.page,
      pageSize: state.pageSize,
    };
    const res = await reportsApi.exportExcel(tab, params);
    const url = window.URL.createObjectURL(res.data);
    const a = document.createElement('a');
    a.href = url;
    a.download = `report-${tab}${currentPageOnly ? '-page' : '-all'}.xlsx`;
    a.click();
    window.URL.revokeObjectURL(url);
  };

  const currentState = tab ? tabStates[tab] : null;
  const monthNames = ['يناير', 'فبراير', 'مارس', 'أبريل', 'مايو', 'يونيو', 'يوليو', 'أغسطس', 'سبتمبر', 'أكتوبر', 'نوفمبر', 'ديسمبر'];

  return (
    <div>
      <div className="page-header">
        <h2 className="page-title">التقارير</h2>
        {tab && (
          <div className="btn-group">
            <button className="btn btn-outline" onClick={() => exportExcel(true)}>تصدير الصفحة الحالية</button>
            <button className="btn btn-primary" onClick={() => exportExcel(false)}>تصدير جميع النتائج</button>
          </div>
        )}
      </div>

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
          <button className="btn btn-primary" onClick={applyFilters}>تطبيق الفلتر</button>
        </div>
      </div>

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

      {!tab && (
        <div className="card mt-2 text-center text-muted" style={{ padding: '2rem' }}>
          اختر قسمًا من الأعلى لعرض المعاملات
        </div>
      )}

      {tab && currentState?.error && <div className="alert alert-error">{currentState.error}</div>}

      {tab && currentState && (
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
                  <td><DateDisplay date={t.incomingDate} /></td>
                  <td><Link to={`/transactions/${t.id}`} className="btn btn-sm">عرض</Link></td>
                </tr>
              ))}
              {!currentState.loading && currentState.loaded && currentState.items.length === 0 && !currentState.error && (
                <tr><td colSpan={8} className="text-center">لا توجد معاملات مطابقة للفلاتر المحددة.</td></tr>
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
        <div className="page-header" style={{ marginBottom: '0.5rem' }}>
          <h3>التحليلات والتقارير التفصيلية</h3>
          <div className="btn-group">
            <button
              type="button"
              className="btn btn-primary"
              onClick={() => loadAnalytics(true)}
              disabled={analyticsLoading}
            >
              {analyticsLoading ? 'جاري التحميل...' : analyticsLoaded ? 'إعادة تحميل التحليلات' : 'تحميل التحليلات'}
            </button>
            <button className="btn btn-outline" onClick={async () => {
              const res = await reportsApi.exportDepartmentIncomingClosedExcel(filterParams());
              const url = window.URL.createObjectURL(res.data);
              const a = document.createElement('a'); a.href = url; a.download = 'department-incoming-closed.xlsx'; a.click();
            }}>تصدير Excel</button>
            <button className="btn btn-outline" onClick={async () => {
              const res = await reportsApi.exportDepartmentIncomingClosedPdf(filterParams());
              const url = window.URL.createObjectURL(res.data);
              const a = document.createElement('a'); a.href = url; a.download = 'department-incoming-closed.pdf'; a.click();
            }}>تصدير PDF</button>
          </div>
        </div>
        <p className="text-muted mb-2">اضغط «تحميل التحليلات» لعرض الجداول أدناه. يُحسب الوارد حسب تاريخ الوارد (الإدارات الصادر لها)، والمغلق حسب تاريخ الإغلاق ضمن الفترة المحددة.</p>
        {analyticsError && <div className="alert alert-error mb-2">{analyticsError}</div>}
        {!analyticsLoaded ? (
          <div className="text-center text-muted" style={{ padding: '1.5rem' }}>لم يتم تحميل التحليلات بعد.</div>
        ) : analyticsLoading ? <div className="loading">جاري التحميل...</div> : (
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
          <h3>حسب التصنيف</h3>
          <table className="data-table">
            <thead><tr><th>التصنيف</th><th>العدد</th></tr></thead>
            <tbody>{categoryReport.map((c, i) => <tr key={i}><td>{c.categoryName}</td><td>{c.count}</td></tr>)}</tbody>
          </table>
        </div>
        <div className="card">
          <h3>حسب الجهات الوارد منها</h3>
          <table className="data-table">
            <thead><tr><th>الجهة</th><th>العدد</th></tr></thead>
            <tbody>{incomingReport.map((p, i) => <tr key={i}><td>{p.partyName}</td><td>{p.transactionCount}</td></tr>)}</tbody>
          </table>
        </div>
        <div className="card">
          <h3>حسب الإدارات الصادر لها</h3>
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
        <h3>التقرير الشهري للوارد والصادر</h3>
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
    </div>
  );
}
