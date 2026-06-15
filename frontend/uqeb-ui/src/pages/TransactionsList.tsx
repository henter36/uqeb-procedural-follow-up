import { useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import { transactionsApi, departmentsApi, categoriesApi, externalPartiesApi } from '../api/services';
import type { TransactionListItem, Department, Category, ExternalParty } from '../api/types';
import { useAuth } from '../context/AuthContext';
import { statusLabels, statusBadgeClass } from '../utils/labels';
import DateDisplay from '../components/DateDisplay';
import DepartmentBadges from '../components/DepartmentBadges';
import { responseTimingBadgeClass } from '../utils/responseTiming';

export default function TransactionsList() {
  const { canEdit } = useAuth();
  const [searchParams] = useSearchParams();
  const [items, setItems] = useState<TransactionListItem[]>([]);
  const [total, setTotal] = useState(0);
  const [departments, setDepartments] = useState<Department[]>([]);
  const [parties, setParties] = useState<ExternalParty[]>([]);
  const [categories, setCategories] = useState<Category[]>([]);
  const [loading, setLoading] = useState(true);
  const [filters, setFilters] = useState({
    incomingNumber: '', outgoingNumber: '', subject: '', status: searchParams.get('status') ?? '',
    incomingSourceType: '', incomingFromPartyId: '', incomingFromDepartmentId: '',
    departmentId: '', categoryId: '', dateFrom: '', dateTo: '',
    responseDueDateFrom: '', responseDueDateTo: '',
    overdueOnly: false, requiresResponse: '', responseCompleted: '',
    responseOverdue: false, hasPendingAssignments: false, hasPartialReplies: false,
    page: 1,
  });

  const load = () => {
    setLoading(true);
    const params: Record<string, unknown> = { pageSize: 20, page: filters.page };
    if (filters.incomingNumber) params.incomingNumber = filters.incomingNumber;
    if (filters.outgoingNumber) params.outgoingNumber = filters.outgoingNumber;
    if (filters.subject) params.subject = filters.subject;
    if (filters.incomingSourceType) params.incomingSourceType = filters.incomingSourceType;
    if (filters.incomingFromPartyId) params.incomingFromPartyId = +filters.incomingFromPartyId;
    if (filters.incomingFromDepartmentId) params.incomingFromDepartmentId = +filters.incomingFromDepartmentId;
    if (filters.status) params.status = filters.status;
    if (filters.departmentId) params.departmentId = +filters.departmentId;
    if (filters.categoryId) params.categoryId = +filters.categoryId;
    if (filters.dateFrom) params.dateFrom = filters.dateFrom;
    if (filters.dateTo) params.dateTo = filters.dateTo;
    if (filters.responseDueDateFrom) params.responseDueDateFrom = filters.responseDueDateFrom;
    if (filters.responseDueDateTo) params.responseDueDateTo = filters.responseDueDateTo;
    if (filters.overdueOnly) params.overdueOnly = true;
    if (filters.requiresResponse === 'true') params.requiresResponse = true;
    if (filters.responseCompleted === 'true') params.responseCompleted = true;
    if (filters.responseCompleted === 'false') params.responseCompleted = false;
    if (filters.responseOverdue) params.responseOverdue = true;
    if (filters.hasPendingAssignments) params.hasPendingAssignments = true;
    if (filters.hasPartialReplies) params.hasPartialReplies = true;

    transactionsApi.search(params)
      .then((res) => { setItems(res.data.items); setTotal(res.data.totalCount); })
      .finally(() => setLoading(false));
  };

  useEffect(() => {
    departmentsApi.getAll().then((r) => setDepartments(r.data));
    externalPartiesApi.getAll().then((r) => setParties(r.data));
    categoriesApi.getAll().then((r) => setCategories(r.data));
  }, []);

  useEffect(() => { load(); }, [filters.page]);

  const handleSearch = (e: React.FormEvent) => {
    e.preventDefault();
    setFilters((f) => ({ ...f, page: 1 }));
    load();
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
                <th>رقم الوارد</th>
                <th>التاريخ</th>
                <th>الموضوع</th>
                <th>الجهة الوارد منها</th>
                <th>التصنيف</th>
                <th>الإدارة</th>
                <th>الحالة</th>
                <th>تاريخ الرد المطلوب</th>
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
