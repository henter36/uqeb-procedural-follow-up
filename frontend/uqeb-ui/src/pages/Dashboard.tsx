import { useEffect, useState } from 'react';
import { Link, Navigate } from 'react-router-dom';
import { reportsApi } from '../api/services';
import type { DashboardSummary, StatusDistribution, TransactionListItem } from '../api/types';
import { useAuth } from '../context/useAuth';
import { usePendingPrintSummary } from '../hooks/usePendingPrintSummary';
import { statusBadgeClass } from '../utils/labels';
import { formatHijriNumeric } from '../utils/dateUtils';
import DepartmentBadges from '../components/DepartmentBadges';
import { PageHeader, StatsSkeleton, EmptyState, ErrorState } from '../components/ui';
import StatusBadge from '../components/ui/StatusBadge';

type StatTone = 'green' | 'red' | 'gold' | 'primary';

type KpiItem = {
  label: string;
  value: number;
  desc: string;
  icon: string;
  tone: StatTone;
  link: string;
};

function statusProgressColor(status: string): string {
  switch (status) {
    case 'Overdue': return 'dashboard-progress-bar--red';
    case 'WaitingForReply': case 'PartiallyReplied': return 'dashboard-progress-bar--gold';
    case 'ResponseCompleted': case 'Closed': return 'dashboard-progress-bar--green';
    default: return 'dashboard-progress-bar--primary';
  }
}

function DashboardCardHeader({
  title,
  subtitle,
  actionLabel,
  actionTo,
}: Readonly<{
  title: string;
  subtitle?: string;
  actionLabel?: string;
  actionTo?: string;
}>) {
  return (
    <div className="dashboard-card-header">
      <div className="dashboard-card-title-group">
        <h2 className="dashboard-card-title">{title}</h2>
        {subtitle ? <p className="dashboard-card-subtitle">{subtitle}</p> : null}
      </div>
      {actionLabel && actionTo ? (
        <Link to={actionTo} className="dashboard-card-action">
          {actionLabel}
          <span aria-hidden="true"> ‹</span>
        </Link>
      ) : null}
    </div>
  );
}

function StatusRows({ rows, total }: Readonly<{ rows: StatusDistribution[]; total: number }>) {
  if (rows.length === 0) {
    return <p className="text-muted" style={{ fontSize: '0.88rem' }}>لا يوجد</p>;
  }
  const denom = total > 0 ? total : 1;
  return (
    <div className="dashboard-mini-list">
      {rows.map((s) => (
        <div key={s.status} className="dashboard-mini-row">
          <div className="dashboard-mini-row-header">
            <span className="dashboard-mini-label"><StatusBadge status={s.status} /></span>
            <span className="dashboard-mini-value">{s.count}</span>
          </div>
          <div className="dashboard-progress">
            <div
              className={`dashboard-progress-bar ${statusProgressColor(s.status)}`}
              style={{ width: `${Math.round((s.count / denom) * 100)}%` }}
            />
          </div>
        </div>
      ))}
    </div>
  );
}

function ActionRequiredTable({ rows }: Readonly<{ rows: TransactionListItem[] }>) {
  if (rows.length === 0) {
    return <EmptyState title="لا توجد معاملات تحتاج إجراء" icon="✅" />;
  }
  return (
    <div className="dashboard-table-wrap">
      <table className="dashboard-table dashboard-action-table">
        <colgroup>
          <col className="col-ref" />
          <col className="col-subject" />
          <col className="col-dept" />
          <col className="col-status" />
          <col className="col-date" />
        </colgroup>
        <thead>
          <tr>
            <th>رقم الوارد</th>
            <th>الموضوع</th>
            <th>الإدارة</th>
            <th>الحالة</th>
            <th className="dashboard-date-cell">التاريخ</th>
          </tr>
        </thead>
        <tbody>
          {rows.map((t) => (
            <tr key={t.id} className={t.isOverdue ? 'row-overdue' : ''}>
              <td className="dashboard-cell-truncate" title={t.incomingNumber}>{t.incomingNumber}</td>
              <td className="dashboard-subject-cell">
                <Link
                  to={`/transactions/${t.id}`}
                  className="dashboard-subject-link"
                  title={t.subject}
                  aria-label={`عرض المعاملة: ${t.subject || t.incomingNumber}`}
                >
                  {t.subject || 'بدون موضوع'}
                </Link>
              </td>
              <td className="dashboard-cell-truncate"><DepartmentBadges names={t.outgoingDepartmentNames} /></td>
              <td><StatusBadge status={t.status} isOverdue={t.isOverdue} /></td>
              <td className="dashboard-date-cell">{formatHijriNumeric(t.incomingDate)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export default function DashboardPage() {
  const { canOperateFollowUpPrint, isDepartmentUser } = useAuth();
  const { pendingTotal } = usePendingPrintSummary(canOperateFollowUpPrint);
  const [data, setData] = useState<DashboardSummary | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    // This dashboard aggregates institution-wide counts across every department; the backend
    // rejects it for DepartmentUser (Policies.ViewOperationalDashboard), so skip the call and
    // send them to their own department-scoped landing page instead — see the render below.
    if (isDepartmentUser) return undefined;

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
  }, [reloadKey, isDepartmentUser]);

  if (isDepartmentUser) {
    return <Navigate to="/department-responses" replace />;
  }

  if (loading) {
    return (
      <div>
        <PageHeader title="لوحة المتابعة" />
        <StatsSkeleton count={8} />
      </div>
    );
  }

  if (error || !data) {
    return (
      <div>
        <PageHeader title="لوحة المتابعة" />
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
                setReloadKey((v) => v + 1);
              }}
            >
              إعادة المحاولة
            </button>
          )}
        />
      </div>
    );
  }

  const kpiItems: KpiItem[] = [
    {
      label: 'المعاملات المفتوحة',
      value: data.totalOpen,
      desc: 'إجمالي المعاملات النشطة',
      icon: '◉',
      tone: 'green',
      link: '/reports?tab=open',
    },
    {
      label: 'متأخرة في الإفادة',
      value: data.responseOverdueCount,
      desc: 'تجاوزت الموعد المحدد',
      icon: '⚠',
      tone: 'red',
      link: '/reports?tab=overdue-responses',
    },
    {
      label: 'بانتظار رد',
      value: data.waitingForReply,
      desc: 'أُرسلت وتنتظر ردًا',
      icon: '↩',
      tone: 'gold',
      link: '/reports?tab=waiting',
    },
    {
      label: 'مطلوب إفادة',
      value: data.requiresResponsePending,
      desc: 'تحتاج اتخاذ إجراء',
      icon: '✏',
      tone: 'primary',
      link: '/reports?tab=response-required',
    },
  ];

  const statusTotal = (data.byStatus ?? []).reduce((sum, s) => sum + s.count, 0);
  const actionRequired = data.actionRequired ?? [];

  return (
    <div className="dashboard-page">
      <PageHeader title="لوحة المتابعة" />

      {/* صف 1 — KPI */}
      <section className="dashboard-kpi-grid" aria-label="مؤشرات لوحة المتابعة">
        {kpiItems.map((item) => (
          <Link
            key={item.label}
            to={item.link}
            className={`dashboard-kpi-card dashboard-kpi-card--${item.tone}`}
          >
            <div className="dashboard-kpi-content">
              <p className="dashboard-kpi-label">{item.label}</p>
              <p className="dashboard-kpi-value">{item.value}</p>
              <p className="dashboard-kpi-description">{item.desc}</p>
            </div>
            <span className="dashboard-kpi-icon" aria-hidden="true">{item.icon}</span>
          </Link>
        ))}
      </section>

      {/* صف 2 — شبكة رئيسية */}
      <div className="dashboard-main-grid">
        <article className="dashboard-card">
          <DashboardCardHeader
            title="أكثر الإدارات تأخراً"
            subtitle="الإدارات الأعلى من حيث المعاملات المتأخرة"
            actionLabel="عرض التقرير"
            actionTo="/reports?tab=overdue-responses"
          />
          {(data.topOverdueDepartments ?? []).length === 0 ? (
            <p className="text-muted" style={{ fontSize: '0.88rem' }}>لا يوجد</p>
          ) : (
            <div className="dashboard-table-wrap">
              <table className="dashboard-table">
                <thead>
                  <tr>
                    <th>الإدارة</th>
                    <th>المتأخر</th>
                  </tr>
                </thead>
                <tbody>
                  {(data.topOverdueDepartments ?? []).map((d) => (
                    <tr key={d.departmentId}>
                      <td>{d.departmentName}</td>
                      <td>
                        <span className={`badge ${statusBadgeClass('Overdue')}`}>{d.overdueCount}</span>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </article>

        <article className="dashboard-card">
          <DashboardCardHeader
            title="المعاملات التي تحتاج إجراء"
            subtitle="آخر المعاملات المطلوب متابعتها أو الرد عليها"
            actionLabel="عرض الكل"
            actionTo="/reports?tab=response-required"
          />
          <ActionRequiredTable rows={actionRequired} />
        </article>
      </div>

      {/* صف 3 — شبكة سفلية */}
      <div className="dashboard-bottom-grid">
        <article className="dashboard-card">
          <DashboardCardHeader
            title="توزيع الحالات"
            subtitle="المعاملات المفتوحة مصنفةً حسب الحالة"
          />
          <StatusRows rows={data.byStatus ?? []} total={statusTotal} />
        </article>

        <article className="dashboard-card">
          <DashboardCardHeader
            title="أكثر الجهات وارداً"
            subtitle="الجهات الأكثر إرسالًا من خارج المنظومة"
          />
          {(data.topIncomingParties ?? []).length === 0 ? (
            <p className="text-muted" style={{ fontSize: '0.88rem' }}>لا يوجد</p>
          ) : (
            <div className="dashboard-incoming-list">
              {(data.topIncomingParties ?? []).map((p) => (
                <div key={p.partyName} className="dashboard-incoming-row">
                  <span className="dashboard-incoming-name">{p.partyName}</span>
                  <span className="dashboard-incoming-count">{p.transactionCount}</span>
                </div>
              ))}
            </div>
          )}
        </article>

        <article className="dashboard-card">
          <DashboardCardHeader
            title="مؤشرات الأداء"
            subtitle="ملخص مؤشرات المتابعة والإنجاز"
          />
          <div className="dashboard-performance-list">
            <div className="dashboard-performance-row">
              <span className="dashboard-performance-icon dashboard-performance-icon--green" aria-hidden="true">◷</span>
              <span className="dashboard-performance-label">متوسط أيام الإنجاز</span>
              <strong className="dashboard-performance-value">{data.averageCompletionDays} يوم</strong>
            </div>
            <div className="dashboard-performance-row">
              <span className="dashboard-performance-icon dashboard-performance-icon--primary" aria-hidden="true">✓</span>
              <span className="dashboard-performance-label">مغلقة هذا الشهر</span>
              <strong className="dashboard-performance-value">{data.closedThisMonth}</strong>
            </div>
            <div className="dashboard-performance-row">
              <span className="dashboard-performance-icon dashboard-performance-icon--gold" aria-hidden="true">◎</span>
              <span className="dashboard-performance-label">جاهزة للإفادة</span>
              <strong className="dashboard-performance-value">{data.readyForResponse}</strong>
            </div>
            <div className="dashboard-performance-row">
              <span className="dashboard-performance-icon dashboard-performance-icon--gold" aria-hidden="true">↕</span>
              <span className="dashboard-performance-label">رد جزئي</span>
              <strong className="dashboard-performance-value">{data.partiallyReplied}</strong>
            </div>
            {canOperateFollowUpPrint && (
              <div className="dashboard-performance-row">
                <span className="dashboard-performance-icon dashboard-performance-icon--orange" aria-hidden="true">⏳</span>
                <span className="dashboard-performance-label">بانتظار تسجيل التعقيب</span>
                <strong className="dashboard-performance-value">
                  <Link to="/follow-up-print/pending" style={{ color: 'inherit' }}>{pendingTotal}</Link>
                </strong>
              </div>
            )}
          </div>
        </article>
      </div>
    </div>
  );
}
