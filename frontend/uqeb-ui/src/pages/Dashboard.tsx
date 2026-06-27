import { useEffect, useState, type ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { reportsApi } from '../api/services';
import type { DashboardSummary, TransactionListItem } from '../api/types';
import { useAuth } from '../context/useAuth';
import { usePendingPrintSummary } from '../hooks/usePendingPrintSummary';
import { statusLabels } from '../utils/labels';
import DateDisplay from '../components/DateDisplay';
import DepartmentBadges from '../components/DepartmentBadges';
import {
  PageHeader, StatCard, StatsSkeleton, EmptyState, ErrorState,
} from '../components/ui';
import StatusBadge from '../components/ui/StatusBadge';

function resolveActionRequiredView(actionRequired: TransactionListItem[] | undefined): 'empty' | 'table' {
  if (actionRequired?.length === 0) return 'empty';
  return 'table';
}

export default function DashboardPage() {
  const { canClose } = useAuth();
  const { pendingTotal } = usePendingPrintSummary(canClose);
  const [data, setData] = useState<DashboardSummary | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    const controller = new AbortController();
    let active = true;

    reportsApi.dashboard({ signal: controller.signal })
      .then((res) => {
        if (!active || controller.signal.aborted) return;
        setData(res.data);
      })
      .catch(() => {
        if (!active || controller.signal.aborted) return;
        setError('تعذر تحميل بيانات اللوحة');
        setData(null);
      })
      .finally(() => {
        if (!active || controller.signal.aborted) return;
        setLoading(false);
      });

    return () => {
      active = false;
      controller.abort();
    };
  }, [reloadKey]);

  const kpiItems = [
    { label: 'معاملات مفتوحة', value: data?.totalOpen ?? 0, color: 'green', link: '/reports?tab=open' },
    { label: 'متأخر في الإفادة', value: data?.responseOverdueCount ?? 0, color: 'red', link: '/reports?tab=overdue-responses' },
    { label: 'بانتظار رد', value: data?.waitingForReply ?? 0, color: 'orange', link: '/reports?tab=waiting' },
    { label: 'مطلوب إفادة', value: data?.requiresResponsePending ?? 0, color: 'purple', link: '/reports?tab=response-required' },
  ];

  const secondaryItems = [
    { label: 'رد جزئي', value: data?.partiallyReplied ?? 0, color: 'cyan', link: '/reports?tab=partial-replies' },
    { label: 'جاهزة للإفادة', value: data?.readyForResponse ?? 0, color: 'green', link: '/transactions?status=ReadyForResponse' },
    { label: 'مغلقة هذا الشهر', value: data?.closedThisMonth ?? 0, color: 'gray', link: '/transactions?status=Closed' },
    { label: 'متوسط أيام الإنجاز', value: data?.averageCompletionDays ?? 0, color: 'blue', link: '/reports', suffix: ' يوم' },
  ];

  const renderSectionRows = <T,>(
    items: T[] | undefined,
    emptyLabel: string,
    colSpan: number,
    renderItem: (item: T, index: number) => ReactNode,
  ): ReactNode => {
    const rows = items ?? [];
    if (rows.length === 0) {
      return <tr><td colSpan={colSpan} className="text-center">{emptyLabel}</td></tr>;
    }
    return rows.map((item, index) => renderItem(item, index));
  };

  if (loading) {
    return (
      <div>
        <PageHeader title="لوحة المتابعة" subtitle="نظرة تشغيلية على المعاملات والإجراءات المطلوبة" />
        <StatsSkeleton count={8} />
      </div>
    );
  }

  if (error || !data) {
    return (
      <div>
        <PageHeader title="لوحة المتابعة" subtitle="نظرة تشغيلية على المعاملات والإجراءات المطلوبة" />
        <ErrorState
          title="تعذر تحميل اللوحة"
          description={error ?? 'تعذر تحميل بيانات اللوحة'}
          action={(
            <button
              type="button"
              className="btn btn-primary"
              onClick={() => {
                setLoading(true);
                setError(null);
                setReloadKey((value) => value + 1);
              }}
            >
              إعادة المحاولة
            </button>
          )}
        />
      </div>
    );
  }

  const actionRequiredView = resolveActionRequiredView(data.actionRequired);

  return (
    <div>
      <PageHeader
        title="لوحة المتابعة"
        subtitle="نظرة تشغيلية على المعاملات والإجراءات المطلوبة"
      />

      {/* صف KPI الرئيسي */}
      <div className="kpi-grid">
        {kpiItems.map((c) => (
          <StatCard
            key={c.label}
            label={c.label}
            value={`${c.value}${c.suffix ?? ''}`}
            color={c.color}
            link={c.link}
          />
        ))}
      </div>

      {/* المؤشرات الثانوية */}
      <div className="stats-grid mb-4">
        {secondaryItems.map((c) => (
          <StatCard
            key={c.label}
            label={c.label}
            value={`${c.value}${c.suffix ?? ''}`}
            color={c.color}
            link={c.link}
          />
        ))}
        {canClose && (
          <StatCard
            label="بانتظار تسجيل التعقيب"
            value={String(pendingTotal)}
            color="orange"
            link="/follow-up-print/pending"
          />
        )}
      </div>

      {/* بطاقات التوزيع والتحليل */}
      <div className="dashboard-grid">
        <div className="card">
          <div className="card-header">
            <h3 className="card-title">أكثر الإدارات تأخراً</h3>
          </div>
          <div className="table-wrapper table-wrapper-spaced">
            <table className="data-table">
              <thead><tr><th>الإدارة</th><th>المتأخر</th></tr></thead>
              <tbody>
                {renderSectionRows(
                  data.topOverdueDepartments,
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
          <div className="card-header">
            <h3 className="card-title">أكثر الجهات وارد منها</h3>
          </div>
          <div className="table-wrapper table-wrapper-spaced">
            <table className="data-table">
              <thead><tr><th>الجهة</th><th>العدد</th></tr></thead>
              <tbody>
                {renderSectionRows(
                  data.topIncomingParties,
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
          <div className="card-header">
            <h3 className="card-title">حسب التصنيف</h3>
          </div>
          <div className="table-wrapper table-wrapper-spaced">
            <table className="data-table">
              <thead><tr><th>التصنيف</th><th>العدد</th></tr></thead>
              <tbody>
                {renderSectionRows(
                  data.byCategory,
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
          <div className="card-header">
            <h3 className="card-title">حسب الحالة</h3>
          </div>
          <div className="table-wrapper table-wrapper-spaced">
            <table className="data-table">
              <thead><tr><th>الحالة</th><th>العدد</th></tr></thead>
              <tbody>
                {renderSectionRows(
                  data.byStatus,
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

      {/* جدول المعاملات التي تحتاج إجراء */}
      <div className="card mt-4">
        <div className="card-header">
          <h3 className="card-title">آخر المعاملات التي تحتاج إجراء</h3>
        </div>
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
                  data.actionRequired,
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
