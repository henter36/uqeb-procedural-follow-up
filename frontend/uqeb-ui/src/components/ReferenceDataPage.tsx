import { useCallback, useEffect, useState } from 'react';
import type { ReactNode } from 'react';
import { isAxiosError } from 'axios';
import type { PagedResult } from '../api/types';
import { defaultListParams, type ReferenceListParams } from './referenceDataTypes';

type Column<T> = {
  key: string;
  label: string;
  sortable?: boolean;
  render: (item: T) => ReactNode;
};

type ReferenceDataPageProps<T> = {
  title: string;
  addLabel: string;
  columns: Column<T>[];
  fetchPage: (params: ReferenceListParams) => Promise<{ data: PagedResult<T> }>;
  getRowId: (item: T) => number;
  renderForm: (ctx: {
    editing: T | null;
    onClose: () => void;
    onSaved: (item: T) => void;
    listParams: ReferenceListParams;
  }) => ReactNode;
  onDeactivate?: (item: T) => Promise<void>;
  canDeactivate?: (item: T) => boolean;
  deactivateLabel?: string;
};

export function ReferenceDataPage<T>({
  title,
  addLabel,
  columns,
  fetchPage,
  getRowId,
  renderForm,
  onDeactivate,
  canDeactivate,
  deactivateLabel = 'تعطيل',
}: ReferenceDataPageProps<T>) {
  const [params, setParams] = useState<ReferenceListParams>(defaultListParams);
  const [items, setItems] = useState<T[]>([]);
  const [total, setTotal] = useState(0);
  const [totalPages, setTotalPages] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);
  const [showForm, setShowForm] = useState(false);
  const [editing, setEditing] = useState<T | null>(null);

  const applyParams = useCallback((updater: (p: ReferenceListParams) => ReferenceListParams) => {
    setLoading(true);
    setParams(updater);
  }, []);

  const load = useCallback(async (activeParams: ReferenceListParams) => {
    setLoading(true);
    setError(null);
    try {
      const res = await fetchPage(activeParams);
      setItems(res.data.items);
      setTotal(res.data.totalCount);
      setTotalPages(res.data.totalPages);
    } catch {
      setError('تعذر تحميل البيانات');
    } finally {
      setLoading(false);
    }
  }, [fetchPage]);

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      try {
        const res = await fetchPage(params);
        if (cancelled) return;
        setItems(res.data.items);
        setTotal(res.data.totalCount);
        setTotalPages(res.data.totalPages);
        setError(null);
      } catch {
        if (!cancelled) setError('تعذر تحميل البيانات');
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => { cancelled = true; };
  }, [fetchPage, params]);

  const handleSort = (key: string) => {
    applyParams((p) => ({
      ...p,
      sortBy: key,
      sortDesc: p.sortBy === key ? !p.sortDesc : false,
      page: 1,
    }));
  };

  const openCreate = () => {
    setEditing(null);
    setShowForm(true);
    setSuccess(null);
  };

  const openEdit = (item: T) => {
    setEditing(item);
    setShowForm(true);
    setSuccess(null);
  };

  const handleSaved = (item: T) => {
    setShowForm(false);
    setEditing(null);
    setSuccess('تم الحفظ بنجاح');
    setItems((prev) => {
      const id = getRowId(item);
      const idx = prev.findIndex((x) => getRowId(x) === id);
      if (idx >= 0) {
        const next = [...prev];
        next[idx] = item;
        return next;
      }
      if (params.page === 1) return [item, ...prev].slice(0, params.pageSize);
      return prev;
    });
    load(params);
  };

  const handleDeactivate = async (item: T) => {
    if (!onDeactivate) return;
    if (!window.confirm(`هل تريد ${deactivateLabel} هذا السجل؟`)) return;
    try {
      await onDeactivate(item);
      setSuccess('تم تحديث الحالة بنجاح');
      load(params);
    } catch (err) {
      setError(isAxiosError(err) ? (err.response?.data as { message?: string })?.message ?? 'تعذر تحديث الحالة' : 'تعذر تحديث الحالة');
    }
  };

  return (
    <div>
      <div className="page-header">
        <h2 className="page-title">{title}</h2>
        <button type="button" className="btn btn-primary" onClick={openCreate}>{addLabel}</button>
      </div>

      <div className="card filter-card reference-toolbar">
        <input
          placeholder="بحث..."
          value={params.search}
          onChange={(e) => applyParams((p) => ({ ...p, search: e.target.value, page: 1 }))}
        />
        <select
          value={params.status}
          onChange={(e) => applyParams((p) => ({ ...p, status: e.target.value, page: 1 }))}
        >
          <option value="all">الكل</option>
          <option value="active">نشط</option>
          <option value="inactive">غير نشط</option>
        </select>
        {params.search && (
          <button type="button" className="btn btn-outline" onClick={() => applyParams((p) => ({ ...p, search: '', page: 1 }))}>
            مسح البحث
          </button>
        )}
      </div>

      {success && <div className="alert alert-success">{success}</div>}
      {error && <div className="alert alert-error">{error}</div>}

      <div className="table-responsive">
        <table className="data-table">
          <thead>
            <tr>
              {columns.map((col) => (
                <th key={col.key}>
                  {col.sortable ? (
                    <button type="button" className="sortable-th-btn" onClick={() => handleSort(col.key)}>
                      {col.label}
                      {params.sortBy === col.key && (params.sortDesc ? ' ▼' : ' ▲')}
                    </button>
                  ) : col.label}
                </th>
              ))}
              <th>إجراءات</th>
            </tr>
          </thead>
          <tbody>
            {loading && (
              <tr><td colSpan={columns.length + 1}>جاري التحميل...</td></tr>
            )}
            {!loading && items.length === 0 && (
              <tr><td colSpan={columns.length + 1}>لا توجد نتائج</td></tr>
            )}
            {!loading && items.map((item) => (
              <tr key={getRowId(item)}>
                {columns.map((col) => <td key={col.key}>{col.render(item)}</td>)}
                <td className="table-actions">
                  <button type="button" className="btn btn-outline btn-sm" onClick={() => openEdit(item)}>تعديل</button>
                  {onDeactivate && (canDeactivate?.(item) ?? true) && (
                    <button type="button" className="btn btn-outline btn-sm" onClick={() => handleDeactivate(item)}>
                      {deactivateLabel}
                    </button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {totalPages > 1 && (
        <div className="pagination">
          <button type="button" className="btn btn-outline" disabled={params.page <= 1} onClick={() => applyParams((p) => ({ ...p, page: p.page - 1 }))}>السابق</button>
          <span>صفحة {params.page} من {totalPages} ({total} سجل)</span>
          <button type="button" className="btn btn-outline" disabled={params.page >= totalPages} onClick={() => applyParams((p) => ({ ...p, page: p.page + 1 }))}>التالي</button>
        </div>
      )}

      {showForm && renderForm({
        editing,
        onClose: () => { setShowForm(false); setEditing(null); },
        onSaved: handleSaved,
        listParams: params,
      })}
    </div>
  );
}
