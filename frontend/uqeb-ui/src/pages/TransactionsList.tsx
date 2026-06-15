import { useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { transactionsApi, departmentsApi, categoriesApi, externalPartiesApi } from '../api/services';
import type { TransactionListItem, Department, Category, ExternalParty } from '../api/types';
import { useAuth } from '../context/AuthContext';
import { statusLabels, statusBadgeClass } from '../utils/labels';
import DateDisplay from '../components/DateDisplay';
import DepartmentBadges from '../components/DepartmentBadges';
import { responseTimingBadgeClass } from '../utils/responseTiming';

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
  sortBy: SortKey;
  sortDesc: boolean;
};

function buildSearchParams(f: FiltersState): Record<string, unknown> {
  const params: Record<string, unknown> = {
    pageSize: 20,
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

function SortableTh({
  columnKey,
  label,
  sortBy,
  sortDesc,
  onSort,
}: {
  columnKey: SortKey;
  label: string;
  sortBy: SortKey;
  sortDesc: boolean;
  onSort: (key: SortKey) => void;
}) {
  const active = sortBy === columnKey;
  return (
    <th>
      <button type="button" className="sortable-th" onClick={() => onSort(columnKey)}>
        <span>{label}</span>
        {active && <span className="sort-indicator" aria-hidden="true">{sortDesc ? '↓' : '↑'}</span>}
      </button>
    </th>
  );
}

export default function TransactionsList() {
  const { canEdit } = useAuth();
  const [searchParams] = useSearchParams();
  const [items, setItems] = useState<TransactionListItem[]>([]);
  const [total, setTotal] = useState(0);
  const [departments, setDepartments] = useState<Department[]>([]);
  const [parties, setParties] = useState<ExternalParty[]>([]);
  const [categories, setCategories] = useState<Category[]>([]);
  const [loading, setLoading] = useState(true);
  const [filters, setFilters] = useState<FiltersState>({
    incomingNumber: '', outgoingNumber: '', subject: '', status: searchParams.get('status') ?? '',
    incomingSourceType: '', incomingFromPartyId: '', incomingFromDepartmentId: '',
    departmentId: '', categoryId: '', dateFrom: '', dateTo: '',
    responseDueDateFrom: '', responseDueDateTo: '',
    overdueOnly: false, requiresResponse: '', responseCompleted: '',
    responseOverdue: false, hasPendingAssignments: false, hasPartialReplies: false,
    page: 1,
    sortBy: 'incomingDate',
    sortDesc: true,
  });

  const load = (override?: Partial<FiltersState>) => {
    const active = { ...filters, ...override };
    setLoading(true);
    transactionsApi.search(buildSearchParams(active))
      .then((res) => { setItems(res.data.items); setTotal(res.data.totalCount); })
      .finally(() => setLoading(false));
  };

  useEffect(() => {
    departmentsApi.getAll().then((r) => setDepartments(r.data));
    externalPartiesApi.getAll().then((r) => setParties(r.data));
    categoriesApi.getAll().then((r) => setCategories(r.data));
  }, []);

  useEffect(() => {
    load();
  }, [filters.page, filters.sortBy, filters.sortDesc]);

  const handleSearch = (e: FormEvent) => {
    e.preventDefault();
    setFilters((f) => ({ ...f, page: 1 }));
    load({ page: 1 });
  };

  const handleSort = (columnKey: SortKey) => {
    setFilters((f) => {
      const isSame = f.sortBy === columnKey;
      return {
        ...f,
        sortBy: columnKey,
        sortDesc: isSame ? !f.sortDesc : DATE_SORT_KEYS.has(columnKey),
        page: 1,
      };
    });
  };

  return (
    <div>
      <div className="page-header">
        <h2 className="page-title">المعاملات</h2>
        {canEdit && <Link to="/transactions/new" className="btn btn-primary">إضافة معاملة</Link>}
      </div>

      <div className="card filter-card">
        <form onSubmit={handleSearch} className="filter-form">
          <input placeholder="رقم الوارد" value={filters.incomingNumber} onChange={(e) => setFilters({ ...filters, incomingNumber: e.target.value })} />
          <input placeholder="الموضوع" value={filters.subject} onChange={(e) => setFilters({ ...filters, subject: e.target.value })} />
          <select value={filters.incomingSourceType} onChange={(e) => setFilters({
            ...filters, incomingSourceType: e.target.value, incomingFromPartyId: '', incomingFromDepartmentId: '',
          })}>
            <option value="">نوع الجهة الوارد: الكل</option>
            <option value="External">خارجية</option>
            <option value="Internal">داخلية</option>
          </select>
          {filters.incomingSourceType === 'External' && (
            <select value={filters.incomingFromPartyId} onChange={(e) => setFilters({ ...filters, incomingFromPartyId: e.target.value })}>
              <option value="">الجهة الخارجية</option>
              {parties.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
            </select>
          )}
          {filters.incomingSourceType === 'Internal' && (
            <select value={filters.incomingFromDepartmentId} onChange={(e) => setFilters({ ...filters, incomingFromDepartmentId: e.target.value })}>
              <option value="">الإدارة الواردة منها</option>
              {departments.map((d) => <option key={d.id} value={d.id}>{d.name}</option>)}
            </select>
          )}
          <select value={filters.status} onChange={(e) => setFilters({ ...filters, status: e.target.value })}>
            <option value="">كل الحالات</option>
            {Object.entries(statusLabels).map(([k, v]) => <option key={k} value={k}>{v}</option>)}
          </select>
          <select value={filters.categoryId} onChange={(e) => setFilters({ ...filters, categoryId: e.target.value })}>
            <option value="">كل التصنيفات</option>
            {categories.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
          </select>
          <select value={filters.departmentId} onChange={(e) => setFilters({ ...filters, departmentId: e.target.value })}>
            <option value="">كل الإدارات (تحويل)</option>
            {departments.map((d) => <option key={d.id} value={d.id}>{d.name}</option>)}
          </select>
          <input type="date" value={filters.dateFrom} onChange={(e) => setFilters({ ...filters, dateFrom: e.target.value })} />
          <input type="date" value={filters.dateTo} onChange={(e) => setFilters({ ...filters, dateTo: e.target.value })} />
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
          <button type="submit" className="btn btn-primary">بحث</button>
        </form>
      </div>

      {loading ? <div className="loading">جاري التحميل...</div> : (
        <>
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
                    <span className="badge badge-gray" style={{ marginLeft: 4 }}>
                      {t.incomingSourceType === 'Internal' ? 'داخلية' : 'خارجية'}
                    </span>
                    {t.incomingFrom || '-'}
                  </td>
                  <td>{t.categoryName || '-'}</td>
                  <td><DepartmentBadges names={t.outgoingDepartmentNames} /></td>
                  <td>
                    <span className={`badge ${statusBadgeClass(t.status, t.isOverdue)}`}>
                      {statusLabels[t.status] || t.status}
                    </span>
                  </td>
                  <td>{t.responseDueDate ? <DateDisplay date={t.responseDueDate} /> : '—'}</td>
                  <td>
                    {t.requiresResponse ? (
                      <span className={`badge ${responseTimingBadgeClass(t.responseTimingStatus)}`}>
                        {t.responseTimingLabel || (t.responseCompleted ? 'مكتمل' : '—')}
                      </span>
                    ) : '—'}
                  </td>
                  <td>{t.lastFollowUpDate ? <DateDisplay date={t.lastFollowUpDate} /> : '—'}</td>
                  <td><Link to={`/transactions/${t.id}`} className="btn btn-sm">عرض</Link></td>
                </tr>
              ))}
              {items.length === 0 && <tr><td colSpan={11} className="text-center">لا توجد معاملات</td></tr>}
            </tbody>
          </table>
          <div className="pagination">
            <span>إجمالي: {total}</span>
            <button disabled={filters.page <= 1} onClick={() => setFilters({ ...filters, page: filters.page - 1 })}>السابق</button>
            <span>صفحة {filters.page}</span>
            <button disabled={items.length < 20} onClick={() => setFilters({ ...filters, page: filters.page + 1 })}>التالي</button>
          </div>
        </>
      )}
    </div>
  );
}
