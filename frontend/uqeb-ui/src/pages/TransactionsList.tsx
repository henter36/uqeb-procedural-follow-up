import { useCallback, useEffect, useMemo, useState } from 'react';
import type { FormEvent } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { transactionsApi, departmentsApi, categoriesApi, externalPartiesApi } from '../api/services';
import type { TransactionListItem, Department, Category, ExternalParty } from '../api/types';
import { useAuth } from '../context/AuthContext';
import { statusLabels } from '../utils/labels';
import DateDisplay from '../components/DateDisplay';
import DepartmentBadges from '../components/DepartmentBadges';
import SearchableSelect, { type SelectOption } from '../components/SearchableSelect';
import { responseTimingBadgeClass } from '../utils/responseTiming';
import { getStorageItem, setStorageItem, removeStorageItem } from '../utils/safeStorage';
import {
  PageHeader, Pagination, TableSkeleton, EmptyState, ErrorState, StatusBadge,
} from '../components/ui';

type SortKey =
  | 'incomingNumber'
  | 'incomingDate'
  | 'subject'
  | 'incomingFrom'
  | 'category'
  | 'priority'
  | 'status'
  | 'responseDueDate'
  | 'createdAt';

const DATE_SORT_KEYS = new Set<SortKey>(['incomingDate', 'responseDueDate', 'createdAt']);

type FiltersState = {
  incomingNumber: string;
  outgoingNumber: string;
  subject: string;
  status: string;
  incomingSourceType: string;
  incomingFromPartyId: string;
  incomingFromDepartmentId: string;
  departmentId: string;
  categoryId: string;
  dateFrom: string;
  dateTo: string;
  responseDueDateFrom: string;
  responseDueDateTo: string;
  overdueOnly: boolean;
  requiresResponse: string;
  responseCompleted: string;
  responseOverdue: boolean;
  hasPendingAssignments: boolean;
  hasPartialReplies: boolean;
  page: number;
  pageSize: number;
  sortBy: SortKey;
  sortDesc: boolean;
};

const DEFAULT_FILTERS: FiltersState = {
  incomingNumber: '', outgoingNumber: '', subject: '', status: '',
  incomingSourceType: '', incomingFromPartyId: '', incomingFromDepartmentId: '',
  departmentId: '', categoryId: '', dateFrom: '', dateTo: '',
  responseDueDateFrom: '', responseDueDateTo: '',
  overdueOnly: false, requiresResponse: '', responseCompleted: '',
  responseOverdue: false, hasPendingAssignments: false, hasPartialReplies: false,
  page: 1,
  pageSize: 20,
  sortBy: 'incomingDate',
  sortDesc: true,
};

const FILTERS_STORAGE_KEY = 'uqeb-transaction-filters';

function buildSearchParams(f: FiltersState): Record<string, unknown> {
  const params: Record<string, unknown> = {
    pageSize: f.pageSize,
    page: f.page,
    sortBy: f.sortBy,
    sortDesc: f.sortDesc,
  };
  if (f.incomingNumber) params.incomingNumber = f.incomingNumber;
  if (f.outgoingNumber) params.outgoingNumber = f.outgoingNumber;
  if (f.subject) params.subject = f.subject;
  if (f.incomingSourceType) params.incomingSourceType = f.incomingSourceType;
  if (f.incomingFromPartyId) params.incomingFromPartyId = +f.incomingFromPartyId;
  if (f.incomingFromDepartmentId) params.incomingFromDepartmentId = +f.incomingFromDepartmentId;
  if (f.status) params.status = f.status;
  if (f.departmentId) params.departmentId = +f.departmentId;
  if (f.categoryId) params.categoryId = +f.categoryId;
  if (f.dateFrom) params.dateFrom = f.dateFrom;
  if (f.dateTo) params.dateTo = f.dateTo;
  if (f.responseDueDateFrom) params.responseDueDateFrom = f.responseDueDateFrom;
  if (f.responseDueDateTo) params.responseDueDateTo = f.responseDueDateTo;
  if (f.overdueOnly) params.overdueOnly = true;
  if (f.requiresResponse === 'true') params.requiresResponse = true;
  if (f.responseCompleted === 'true') params.responseCompleted = true;
  if (f.responseCompleted === 'false') params.responseCompleted = false;
  if (f.responseOverdue) params.responseOverdue = true;
  if (f.hasPendingAssignments) params.hasPendingAssignments = true;
  if (f.hasPartialReplies) params.hasPartialReplies = true;
  return params;
}

function clampPage(page: number, totalCount: number, pageSize: number): number {
  const totalPages = Math.max(1, Math.ceil(totalCount / pageSize));
  return Math.min(Math.max(1, page), totalPages);
}

type SortableThProps = Readonly<{
  columnKey: SortKey;
  label: string;
  sortBy: SortKey;
  sortDesc: boolean;
  onSort: (key: SortKey) => void;
}>;

function getAriaSort(active: boolean, sortDesc: boolean): 'none' | 'ascending' | 'descending' {
  if (!active) return 'none';
  return sortDesc ? 'descending' : 'ascending';
}

function SortableTh({
  columnKey,
  label,
  sortBy,
  sortDesc,
  onSort,
}: SortableThProps) {
  const active = sortBy === columnKey;
  return (
    <th aria-sort={getAriaSort(active, sortDesc)}>
      <button type="button" className="sortable-th" onClick={() => onSort(columnKey)}>
        <span>{label}</span>
        {active && <span className="sort-indicator" aria-hidden="true">{sortDesc ? '↓' : '↑'}</span>}
      </button>
    </th>
  );
}

function loadSavedFilters(statusFromUrl: string): FiltersState {
  const saved = getStorageItem(FILTERS_STORAGE_KEY);
  if (saved) {
    try {
      const parsed = JSON.parse(saved) as Partial<FiltersState>;
      return { ...DEFAULT_FILTERS, ...parsed, status: statusFromUrl || parsed.status || '', page: 1 };
    } catch {
      return { ...DEFAULT_FILTERS, status: statusFromUrl };
    }
  }
  return { ...DEFAULT_FILTERS, status: statusFromUrl };
}

export default function TransactionsList() {
  const { canEdit, isAdmin } = useAuth();
  const [searchParams] = useSearchParams();
  const [items, setItems] = useState<TransactionListItem[]>([]);
  const [total, setTotal] = useState(0);
  const [departments, setDepartments] = useState<Department[]>([]);
  const [parties, setParties] = useState<ExternalParty[]>([]);
  const [categories, setCategories] = useState<Category[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [filtersExpanded, setFiltersExpanded] = useState(false);
  const initialFilters = loadSavedFilters(searchParams.get('status') ?? '');
  const [filters, setFilters] = useState<FiltersState>(initialFilters);
  const [searchQuery, setSearchQuery] = useState<FiltersState>(initialFilters);

  const partyOptions: SelectOption[] = useMemo(
    () => parties.map((p) => ({ id: p.id, name: p.name, isActive: p.isActive })),
    [parties],
  );
  const departmentOptions: SelectOption[] = useMemo(
    () => departments.map((d) => ({ id: d.id, name: d.name, isActive: d.isActive, subLabel: d.code })),
    [departments],
  );
  const categoryOptions: SelectOption[] = useMemo(
    () => categories.map((c) => ({ id: c.id, name: c.name, isActive: c.isActive, subLabel: c.code })),
    [categories],
  );

  const effectivePage = clampPage(searchQuery.page, total, searchQuery.pageSize);
  const [retryNonce, setRetryNonce] = useState(0);

  useEffect(() => {
    let cancelled = false;

    transactionsApi.search(buildSearchParams(searchQuery))
      .then((res) => {
        if (cancelled) return;

        const totalCount = res.data.totalCount;
        const correctedPage = clampPage(searchQuery.page, totalCount, searchQuery.pageSize);

        setItems(res.data.items);
        setTotal(totalCount);

        if (correctedPage !== searchQuery.page) {
          const corrected = { ...searchQuery, page: correctedPage };
          setFilters(corrected);
          setSearchQuery(corrected);
        }
      })
      .catch(() => {
        if (cancelled) return;
        setItems([]);
        setTotal(0);
        setLoadError('تعذر تحميل قائمة المعاملات. تحقق من الاتصال وحاول مرة أخرى.');
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [searchQuery, retryNonce]);

  const applyQuery = useCallback((next: FiltersState) => {
    setLoading(true);
    setLoadError(null);
    setFilters(next);
    setSearchQuery(next);
  }, []);

  const handleSearch = (e: FormEvent) => {
    e.preventDefault();
    const next = { ...filters, page: 1 };
    setStorageItem(FILTERS_STORAGE_KEY, JSON.stringify(next));
    applyQuery(next);
  };

  const handleReset = () => {
    const reset = { ...DEFAULT_FILTERS, status: searchParams.get('status') ?? '' };
    removeStorageItem(FILTERS_STORAGE_KEY);
    applyQuery(reset);
  };

  const handleSort = (columnKey: SortKey) => {
    const isSame = filters.sortBy === columnKey;
    const next = {
      ...filters,
      sortBy: columnKey,
      sortDesc: isSame ? !filters.sortDesc : DATE_SORT_KEYS.has(columnKey),
      page: 1,
    };
    applyQuery(next);
  };

  const handlePageChange = (page: number) => {
    applyQuery({ ...filters, page });
  };

  const handlePageSizeChange = (pageSize: number) => {
    applyQuery({ ...filters, pageSize, page: 1 });
  };

  const handleRetry = () => {
    setLoading(true);
    setLoadError(null);
    setRetryNonce((nonce) => nonce + 1);
  };

  useEffect(() => {
    departmentsApi.getAll().then((r) => setDepartments(r.data));
    externalPartiesApi.getAll().then((r) => setParties(r.data));
    categoriesApi.getAll().then((r) => setCategories(r.data));
  }, []);

  const listContent = (() => {
    if (loading) return <TableSkeleton rows={8} cols={11} />;
    if (loadError) {
      return (
        <ErrorState
          title="تعذر تحميل المعاملات"
          description={loadError}
          action={(
            <button type="button" className="btn btn-primary" onClick={handleRetry}>
              إعادة المحاولة
            </button>
          )}
        />
      );
    }
    if (items.length === 0) {
      return (
        <EmptyState
          title="لا توجد معاملات"
          description="جرّب تعديل معايير البحث أو إضافة معاملة جديدة"
          action={canEdit ? <Link to="/transactions/new" className="btn btn-primary">إضافة معاملة</Link> : undefined}
        />
      );
    }
    return (
      <>
        <div className="table-wrapper">
          <table className="data-table">
            <thead>
              <tr>
                <SortableTh columnKey="incomingNumber" label="رقم الوارد" sortBy={filters.sortBy} sortDesc={filters.sortDesc} onSort={handleSort} />
                <SortableTh columnKey="incomingDate" label="التاريخ" sortBy={filters.sortBy} sortDesc={filters.sortDesc} onSort={handleSort} />
                <SortableTh columnKey="subject" label="الموضوع" sortBy={filters.sortBy} sortDesc={filters.sortDesc} onSort={handleSort} />
                <SortableTh columnKey="incomingFrom" label="الجهة الوارد منها" sortBy={filters.sortBy} sortDesc={filters.sortDesc} onSort={handleSort} />
                <SortableTh columnKey="category" label="التصنيف" sortBy={filters.sortBy} sortDesc={filters.sortDesc} onSort={handleSort} />
                <th>الإدارة</th>
                <SortableTh columnKey="status" label="الحالة" sortBy={filters.sortBy} sortDesc={filters.sortDesc} onSort={handleSort} />
                <SortableTh columnKey="responseDueDate" label="تاريخ الرد المطلوب" sortBy={filters.sortBy} sortDesc={filters.sortDesc} onSort={handleSort} />
                <th>حالة الرد</th>
                <th>آخر تعقيب</th>
                <th>إجراءات</th>
              </tr>
            </thead>
            <tbody>
              {items.map((t) => (
                <tr key={t.id} className={t.isOverdue ? 'row-overdue' : ''}>
                  <td>{t.incomingNumber}</td>
                  <td><DateDisplay date={t.incomingDate} /></td>
                  <td>{t.subject}</td>
                  <td>
                    <span className="badge badge-gray badge-gap">
                      {t.incomingSourceType === 'Internal' ? 'داخلية' : 'خارجية'}
                    </span>
                    {t.incomingFrom || '-'}
                  </td>
                  <td>{t.categoryName || '-'}</td>
                  <td><DepartmentBadges names={t.outgoingDepartmentNames} /></td>
                  <td><StatusBadge status={t.status} isOverdue={t.isOverdue} /></td>
                  <td>{t.responseDueDate ? <DateDisplay date={t.responseDueDate} /> : '—'}</td>
                  <td>
                    {t.requiresResponse ? (
                      <span className={`badge ${responseTimingBadgeClass(t.responseTimingStatus)}`}>
                        {t.responseTimingLabel || (t.responseCompleted ? 'مكتمل' : '—')}
                      </span>
                    ) : '—'}
                  </td>
                  <td>{t.lastFollowUpDate ? <DateDisplay date={t.lastFollowUpDate} /> : '—'}</td>
                  <td><Link to={`/transactions/${t.id}`} className="btn btn-sm btn-outline">عرض</Link></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <Pagination
          page={effectivePage}
          pageSize={searchQuery.pageSize}
          total={total}
          itemCount={items.length}
          onPageChange={handlePageChange}
          onPageSizeChange={handlePageSizeChange}
        />
      </>
    );
  })();

  return (
    <div>
      <PageHeader
        title="المعاملات"
        subtitle="بحث وفلترة وإدارة جميع المعاملات"
        actions={(
          <>
            {isAdmin && (
              <Link to="/transactions/import" className="btn btn-secondary">
                استيراد من Excel
              </Link>
            )}
            {canEdit && <Link to="/transactions/new" className="btn btn-primary">إضافة معاملة</Link>}
          </>
        )}
      />

      <div className="card filter-card">
        <div className="filter-bar-header">
          <span className="filter-bar-title">البحث والفلاتر</span>
          <div className="btn-group">
            <button type="button" className="btn btn-ghost btn-sm" onClick={() => setFiltersExpanded((e) => !e)}>
              {filtersExpanded ? 'إخفاء الفلاتر المتقدمة' : 'فلاتر متقدمة'}
            </button>
            <button type="button" className="btn btn-ghost btn-sm" onClick={handleReset}>
              إعادة ضبط
            </button>
          </div>
        </div>
        <form onSubmit={handleSearch} className="filter-form">
          <input
            placeholder="رقم الوارد"
            aria-label="رقم الوارد"
            value={filters.incomingNumber}
            onChange={(e) => setFilters({ ...filters, incomingNumber: e.target.value })}
          />
          <input
            placeholder="رقم الصادر"
            aria-label="رقم الصادر"
            value={filters.outgoingNumber}
            onChange={(e) => setFilters({ ...filters, outgoingNumber: e.target.value })}
          />
          <input
            placeholder="الموضوع"
            aria-label="الموضوع"
            value={filters.subject}
            onChange={(e) => setFilters({ ...filters, subject: e.target.value })}
          />
          <select
            aria-label="الحالة"
            value={filters.status}
            onChange={(e) => setFilters({ ...filters, status: e.target.value })}
          >
            <option value="">كل الحالات</option>
            {Object.entries(statusLabels).map(([k, v]) => <option key={k} value={k}>{v}</option>)}
          </select>
          <SearchableSelect
            label="التصنيف"
            value={filters.categoryId ? +filters.categoryId : ''}
            onChange={(id) => setFilters({ ...filters, categoryId: id === '' ? '' : String(id) })}
            options={categoryOptions}
            allowClear
          />
          <button type="submit" className="btn btn-primary">بحث</button>

          {filtersExpanded && (
            <div className="filter-advanced">
              <select
                aria-label="نوع الجهة الوارد"
                value={filters.incomingSourceType}
                onChange={(e) => setFilters({
                  ...filters, incomingSourceType: e.target.value, incomingFromPartyId: '', incomingFromDepartmentId: '',
                })}
              >
                <option value="">نوع الجهة الوارد: الكل</option>
                <option value="External">خارجية</option>
                <option value="Internal">داخلية</option>
              </select>
              {filters.incomingSourceType === 'External' && (
                <SearchableSelect
                  label="الجهة الخارجية"
                  value={filters.incomingFromPartyId ? +filters.incomingFromPartyId : ''}
                  onChange={(id) => setFilters({ ...filters, incomingFromPartyId: id === '' ? '' : String(id) })}
                  options={partyOptions}
                  allowClear
                />
              )}
              {filters.incomingSourceType === 'Internal' && (
                <SearchableSelect
                  label="الإدارة الواردة منها"
                  value={filters.incomingFromDepartmentId ? +filters.incomingFromDepartmentId : ''}
                  onChange={(id) => setFilters({ ...filters, incomingFromDepartmentId: id === '' ? '' : String(id) })}
                  options={departmentOptions}
                  allowClear
                />
              )}
              <SearchableSelect
                label="الإدارة (تحويل)"
                value={filters.departmentId ? +filters.departmentId : ''}
                onChange={(id) => setFilters({ ...filters, departmentId: id === '' ? '' : String(id) })}
                options={departmentOptions}
                allowClear
              />
              <input type="date" aria-label="من تاريخ" value={filters.dateFrom} onChange={(e) => setFilters({ ...filters, dateFrom: e.target.value })} />
              <input type="date" aria-label="إلى تاريخ" value={filters.dateTo} onChange={(e) => setFilters({ ...filters, dateTo: e.target.value })} />
              <input type="date" aria-label="موعد الرد من" value={filters.responseDueDateFrom} onChange={(e) => setFilters({ ...filters, responseDueDateFrom: e.target.value })} />
              <input type="date" aria-label="موعد الرد إلى" value={filters.responseDueDateTo} onChange={(e) => setFilters({ ...filters, responseDueDateTo: e.target.value })} />
              <select value={filters.requiresResponse} onChange={(e) => setFilters({ ...filters, requiresResponse: e.target.value })}>
                <option value="">الإفادة: الكل</option>
                <option value="true">مطلوب إفادة</option>
              </select>
              <select value={filters.responseCompleted} onChange={(e) => setFilters({ ...filters, responseCompleted: e.target.value })}>
                <option value="">حالة الإفادة: الكل</option>
                <option value="false">لم تتم الإفادة</option>
                <option value="true">تمت الإفادة</option>
              </select>
              <label className="checkbox-label"><input type="checkbox" checked={filters.overdueOnly} onChange={(e) => setFilters({ ...filters, overdueOnly: e.target.checked })} /> متأخر</label>
              <label className="checkbox-label"><input type="checkbox" checked={filters.responseOverdue} onChange={(e) => setFilters({ ...filters, responseOverdue: e.target.checked })} /> متأخر في الإفادة</label>
              <label className="checkbox-label"><input type="checkbox" checked={filters.hasPendingAssignments} onChange={(e) => setFilters({ ...filters, hasPendingAssignments: e.target.checked })} /> تحويلات معلقة</label>
              <label className="checkbox-label"><input type="checkbox" checked={filters.hasPartialReplies} onChange={(e) => setFilters({ ...filters, hasPartialReplies: e.target.checked })} /> رد جزئي</label>
            </div>
          )}
        </form>
      </div>

      {listContent}
    </div>
  );
}
