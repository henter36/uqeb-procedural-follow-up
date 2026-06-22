import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { dashboardApi } from '../api/services';
import type { DashboardSummary, TransactionListItem } from '../api/types';
import { statusLabels } from '../utils/labels';
import DateDisplay from '../components/DateDisplay';
import DepartmentBadges from '../components/DepartmentBadges';
import {
  PageHeader, StatCard, Alert, StatsSkeleton, TableSkeleton, EmptyState, LoadingInline,
} from '../components/ui';
import StatusBadge from '../components/ui/StatusBadge';

const SECTION_UNAVAILABLE = 'لا توجد بيانات متاحة';

type ActionRequiredView = 'loading' | 'empty' | 'table';

function resolveActionRequiredView(
  detailsLoading: boolean,
  actionRequired: TransactionListItem[] | undefined,
): ActionRequiredView {
  if (detailsLoading && actionRequired === undefined) {
    return 'loading';
  }
  if (actionRequired !== undefined && actionRequired.length === 0) {
    return 'empty';
  }
  return 'table';
}

export default function DashboardPage() {
  const [data, setData] = useState<DashboardSummary | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [detailsLoading, setDetailsLoading] = useState(false);
  const [detailsError, setDetailsError] = useState<string | null>(null);
  const [detailsLoaded, setDetailsLoaded] = useState(false);
  const detailsStartedRef = useRef(false);

  useEffect(() => {
    const controller = new AbortController();
    let active = true;

    dashboardApi.summary()
      .then((res) => {
        if (!active || controller.signal.aborted) return;
        setData(res.data);
      })
      .catch(() => {
        if (!active || controller.signal.aborted) return;
        setError('تعذر تحميل بيانات اللوحة');
      })
      .finally(() => {
        if (!active || controller.signal.aborted) return;
        setLoading(false);
      });

    return () => {
      active = false;
      controller.abort();
    };
  }, []);

  const loadDetails = useCallback(async (signal?: AbortSignal) => {
    setDetailsLoading(true);
    setDetailsError(null);

    const [actionRequired, topOverdue, topIncoming, byCategory, byStatus] = await Promise.allSettled([
      dashboardApi.actionRequired(),
      dashboardApi.topOverdueDepartments(),
      dashboardApi.topIncomingParties(),
      dashboardApi.categoryDistribution(),
      dashboardApi.statusDistribution(),
    ]);

    if (signal?.aborted) return;

    const errors: string[] = [];
    const next: Partial<DashboardSummary> = {};

    if (actionRequired.status === 'fulfilled') next.actionRequired = actionRequired.value.data ?? [];
    else errors.push('المعاملات التي تحتاج إجراء');

    if (topOverdue.status === 'fulfilled') next.topOverdueDepartments = topOverdue.value.data ?? [];
    else errors.push('الإدارات المتأخرة');

    if (topIncoming.status === 'fulfilled') next.topIncomingParties = topIncoming.value.data ?? [];
    else errors.push('الجهات الوارد منها');

    if (byCategory.status === 'fulfilled') next.byCategory = byCategory.value.data ?? [];
    else errors.push('توزيع التصنيفات');

    if (byStatus.status === 'fulfilled') next.byStatus = byStatus.value.data ?? [];
    else errors.push('توزيع الحالات');

    if (signal?.aborted) return;

    setData((prev) => ({ ...(prev ?? {}), ...next } as DashboardSummary));
    setDetailsLoaded(true);
    if (errors.length > 0) {
      setDetailsError(`تعذر تحميل: ${errors.join('، ')}`);
    }
    setDetailsLoading(false);
  }, []);

  useEffect(() => {
    if (loading || error || !data || detailsStartedRef.current) return;

    detailsStartedRef.current = true;
    const controller = new AbortController();
    loadDetails(controller.signal);

    return () => {
      controller.abort();
    };
  }, [loading, error, data, loadDetails]);

  const cardItems = [
    { label: 'معاملات مفتوحة', value: data?.totalOpen ?? 0, color: 'blue', link: '/reports?tab=open' },
    { label: 'مطلوب إفادة', value: data?.requiresResponsePending ?? 0, color: 'purple', link: '/reports?tab=response-required' },
    { label: 'متأخر في الإفادة', value: data?.responseOverdueCount ?? 0, color: 'red', link: '/reports?tab=overdue-responses' },
    { label: 'بانتظار رد', value: data?.waitingForReply ?? 0, color: 'orange', link: '/reports?tab=waiting' },
    { label: 'رد جزئي', value: data?.partiallyReplied ?? 0, color: 'cyan', link: '/reports?tab=partial-replies' },
    { label: 'جاهزة للإفادة', value: data?.readyForResponse ?? 0, color: 'green', link: '/transactions?status=ReadyForResponse' },
    { label: 'مغلقة هذا الشهر', value: data?.closedThisMonth ?? 0, color: 'gray', link: '/transactions?status=Closed' },
    { label: 'متوسط أيام الإنجاز', value: data?.averageCompletionDays ?? 0, color: 'blue', link: '/reports', suffix: ' يوم' },
  ];

  const renderSectionRows = <T,>(
    loaded: boolean,
    items: T[],
    emptyLabel: string,
    colSpan: number,
    renderItem: (item: T, index: number) => ReactNode,
  ): ReactNode => {
    if (detailsLoading && !loaded) {
      return <tr><td colSpan={colSpan} className="text-center"><LoadingInline /></td></tr>;
    }
    if (!loaded) {
      return <tr><td colSpan={colSpan} className="text-center">{SECTION_UNAVAILABLE}</td></tr>;
    }
    if (items.length === 0) {
      return <tr><td colSpan={colSpan} className="text-center">{emptyLabel}</td></tr>;
    }
    return items.map(renderItem);
  };

  const actionRequiredView = resolveActionRequiredView(detailsLoading, data?.actionRequired);

  return (
    <div>
      <PageHeader
        title="لوحة المتابعة"
        subtitle="نظرة تشغيلية على المعاملات والإجراءات المطلوبة"
      />

      {error && <Alert variant="error">{error}</Alert>}

      {loading ? (
        <StatsSkeleton count={8} />
      ) : (
        <div className="stats-grid">
          {cardItems.map((c) => (
            <StatCard
              key={c.label}
              label={c.label}
              value={`${c.value}${c.suffix ?? ''}`}
              color={c.color}
              link={c.link}
            />
          ))}
        </div>
      )}

      {(detailsLoading || detailsError) && (
        <div className="mt-4 details-banner-row">
          {detailsLoading && <Alert variant="info"><LoadingInline label="جاري تحميل تفاصيل اللوحة..." /></Alert>}
          {detailsError && <Alert variant="warning">{detailsError}</Alert>}
          {detailsLoaded && !detailsLoading && (
            <button type="button" className="btn btn-secondary btn-sm" onClick={() => loadDetails()}>
              إعادة تحميل التفاصيل
            </button>
          )}
        </div>
      )}

      <div className="dashboard-grid mt-4">
        <div className="card">
          <h3>أكثر الإدارات تأخراً</h3>
          <div className="table-wrapper table-wrapper-spaced">
            <table className="data-table">
              <thead><tr><th>الإدارة</th><th>المتأخر</th></tr></thead>
              <tbody>
                {renderSectionRows(
                  data?.topOverdueDepartments !== undefined,
                  data?.topOverdueDepartments ?? [],
                  'لا يوجد',
                  2,
                  (d) => (
                    <tr key={d.departmentId}>
                      <td>{d.departmentName}</td>
                      <td><span className="badge badge-red">{d.overdueCount}</span></td>
                    </tr>
                  ),
                )}
              </tbody>
            </table>
          </div>
        </div>

        <div className="card">
          <h3>أكثر الجهات وارد منها</h3>
          <div className="table-wrapper table-wrapper-spaced">
            <table className="data-table">
              <thead><tr><th>الجهة</th><th>العدد</th></tr></thead>
              <tbody>
                {renderSectionRows(
                  data?.topIncomingParties !== undefined,
                  data?.topIncomingParties ?? [],
                  'لا يوجد',
                  2,
                  (p, i) => (
                    <tr key={i}><td>{p.partyName}</td><td>{p.transactionCount}</td></tr>
                  ),
                )}
              </tbody>
            </table>
          </div>
        </div>

        <div className="card">
          <h3>حسب التصنيف</h3>
          <div className="table-wrapper table-wrapper-spaced">
            <table className="data-table">
              <thead><tr><th>التصنيف</th><th>العدد</th></tr></thead>
              <tbody>
                {renderSectionRows(
                  data?.byCategory !== undefined,
                  data?.byCategory ?? [],
                  'لا يوجد',
                  2,
                  (c, i) => (
                    <tr key={i}><td>{c.categoryName}</td><td>{c.count}</td></tr>
                  ),
                )}
              </tbody>
            </table>
          </div>
        </div>

        <div className="card">
          <h3>حسب الحالة</h3>
          <div className="table-wrapper table-wrapper-spaced">
            <table className="data-table">
              <thead><tr><th>الحالة</th><th>العدد</th></tr></thead>
              <tbody>
                {renderSectionRows(
                  data?.byStatus !== undefined,
                  data?.byStatus ?? [],
                  'لا يوجد',
                  2,
                  (s, i) => (
                    <tr key={i}><td>{statusLabels[s.status] || s.status}</td><td>{s.count}</td></tr>
                  ),
                )}
              </tbody>
            </table>
          </div>
        </div>
      </div>

      <div className="card mt-4">
        <h3>آخر المعاملات التي تحتاج إجراء</h3>
        {actionRequiredView === 'loading' && (
          <TableSkeleton rows={4} cols={6} />
        )}
        {actionRequiredView === 'empty' && (
          <EmptyState title="لا توجد معاملات تحتاج إجراء" icon="✅" />
        )}
        {actionRequiredView === 'table' && (
          <div className="table-wrapper table-wrapper-spaced">
            <table className="data-table">
              <thead>
                <tr>
                  <th>رقم الوارد</th>
                  <th>الموضوع</th>
                  <th>الإدارة</th>
                  <th>الحالة</th>
                  <th>التاريخ</th>
                  <th>عرض</th>
                </tr>
              </thead>
              <tbody>
                {renderSectionRows(
                  data?.actionRequired !== undefined,
                  data?.actionRequired ?? [],
                  'لا توجد',
                  6,
                  (t) => (
                    <tr key={t.id} className={t.isOverdue ? 'row-overdue' : ''}>
                      <td>{t.incomingNumber}</td>
                      <td>{t.subject}</td>
                      <td><DepartmentBadges names={t.outgoingDepartmentNames} /></td>
                      <td><StatusBadge status={t.status} isOverdue={t.isOverdue} /></td>
                      <td><DateDisplay date={t.incomingDate} /></td>
                      <td><Link to={`/transactions/${t.id}`} className="btn btn-sm btn-outline">عرض</Link></td>
                    </tr>
                  ),
                )}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
}
