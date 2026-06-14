import { useCallback, useEffect, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { dashboardApi } from '../api/services';
import type { DashboardDetails, DashboardSummary } from '../api/types';
import { statusLabels, statusBadgeClass } from '../utils/labels';
import DateDisplay from '../components/DateDisplay';
import DepartmentBadges from '../components/DepartmentBadges';

const emptyDetails: DashboardDetails = {
  topOverdueDepartments: [],
  topIncomingParties: [],
  byCategory: [],
  byStatus: [],
  actionRequired: [],
};

const SUMMARY_CACHE_KEY = 'dashboard-summary-cache';
const SUMMARY_CACHE_TTL_MS = 60_000;

function readSummaryCache(): DashboardSummary | null {
  try {
    const raw = sessionStorage.getItem(SUMMARY_CACHE_KEY);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as { savedAt: number; data: DashboardSummary };
    if (Date.now() - parsed.savedAt > SUMMARY_CACHE_TTL_MS) return null;
    return parsed.data;
  } catch {
    return null;
  }
}

function writeSummaryCache(data: DashboardSummary) {
  try {
    sessionStorage.setItem(SUMMARY_CACHE_KEY, JSON.stringify({ savedAt: Date.now(), data }));
  } catch {
    // ignore storage errors
  }
}

export default function DashboardPage() {
  const cachedSummary = readSummaryCache();
  const [cards, setCards] = useState<DashboardSummary | null>(cachedSummary);
  const [details, setDetails] = useState<DashboardDetails | null>(null);
  const [cardsLoading, setCardsLoading] = useState(!cachedSummary);
  const [detailsLoading, setDetailsLoading] = useState(false);
  const [detailsLoaded, setDetailsLoaded] = useState(false);
  const [cardsError, setCardsError] = useState<string | null>(null);
  const [detailsError, setDetailsError] = useState<string | null>(null);

  const fetchedRef = useRef(false);

  const loadDetails = useCallback(async () => {
    setDetailsLoading(true);
    setDetailsError(null);

    const [actionRequired, topOverdue, topIncoming, byCategory, byStatus] = await Promise.allSettled([
      dashboardApi.actionRequired(),
      dashboardApi.topOverdueDepartments(),
      dashboardApi.topIncomingParties(),
      dashboardApi.categoryDistribution(),
      dashboardApi.statusDistribution(),
    ]);

    const next: DashboardDetails = { ...emptyDetails };
    const errors: string[] = [];

    if (actionRequired.status === 'fulfilled') next.actionRequired = actionRequired.value.data;
    else errors.push('المعاملات التي تحتاج إجراء');

    if (topOverdue.status === 'fulfilled') next.topOverdueDepartments = topOverdue.value.data;
    else errors.push('الإدارات المتأخرة');

    if (topIncoming.status === 'fulfilled') next.topIncomingParties = topIncoming.value.data;
    else errors.push('الجهات الوارد منها');

    if (byCategory.status === 'fulfilled') next.byCategory = byCategory.value.data;
    else errors.push('توزيع التصنيفات');

    if (byStatus.status === 'fulfilled') next.byStatus = byStatus.value.data;
    else errors.push('توزيع الحالات');

    setDetails(next);
    setDetailsLoaded(true);
    if (errors.length > 0) {
      setDetailsError(`تعذر تحميل: ${errors.join('، ')}`);
    }
    setDetailsLoading(false);
  }, []);

  useEffect(() => {
    if (fetchedRef.current) return;
    fetchedRef.current = true;

    let active = true;
    dashboardApi.summary()
      .then((res) => {
        if (!active) return;
        setCards(res.data);
        writeSummaryCache(res.data);
      })
      .catch(() => { if (active) setCardsError('فشل تحميل البيانات'); })
      .finally(() => { if (active) setCardsLoading(false); });

    return () => { active = false; };
  }, []);

  if (cardsLoading && !cards) return <div className="loading">جاري التحميل...</div>;
  if (cardsError || !cards) return <div className="alert alert-error">{cardsError || 'فشل تحميل البيانات'}</div>;

  const cardItems = [
    { label: 'معاملات مفتوحة', value: cards.totalOpen, color: 'blue', link: '/transactions' },
    { label: 'مطلوب إفادة', value: cards.requiresResponsePending, color: 'purple', link: '/reports' },
    { label: 'متأخر في الإفادة', value: cards.responseOverdueCount, color: 'red', link: '/reports' },
    { label: 'بانتظار رد', value: cards.waitingForReply, color: 'orange', link: '/reports' },
    { label: 'رد جزئي', value: cards.partiallyReplied, color: 'cyan', link: '/reports' },
    { label: 'جاهزة للإفادة', value: cards.readyForResponse, color: 'green', link: '/transactions?status=ReadyForResponse' },
    { label: 'مغلقة هذا الشهر', value: cards.closedThisMonth, color: 'gray', link: '/transactions?status=Closed' },
    { label: 'متوسط أيام الإنجاز', value: cards.averageCompletionDays, color: 'blue', link: '/reports', suffix: ' يوم' },
  ];

  const data = details ?? emptyDetails;

  return (
    <div>
      <h2 className="page-title">لوحة المتابعة</h2>

      {cardsLoading && <div className="alert alert-info">جاري تحديث الأرقام...</div>}

      <div className="stats-grid">
        {cardItems.map((c) => (
          <Link key={c.label} to={c.link} className={`stat-card stat-${c.color}`}>
            <div className="stat-value">{c.value}{c.suffix || ''}</div>
            <div className="stat-label">{c.label}</div>
          </Link>
        ))}
      </div>

      <div className="mt-4" style={{ display: 'flex', gap: '0.75rem', alignItems: 'center', flexWrap: 'wrap' }}>
        <button
          type="button"
          className="btn btn-secondary"
          disabled={detailsLoading}
          onClick={loadDetails}
        >
          {detailsLoading ? 'جاري تحميل التفاصيل...' : detailsLoaded ? 'إعادة تحميل التفاصيل' : 'تحميل تفاصيل اللوحة'}
        </button>
        {detailsError && <span className="alert alert-warning" style={{ margin: 0 }}>{detailsError}</span>}
      </div>

      {detailsLoaded && (
        <>
          <div className="dashboard-grid mt-4">
            <div className="card">
              <h3>أكثر الإدارات تأخراً</h3>
              <table className="data-table">
                <thead><tr><th>الإدارة</th><th>المتأخر</th></tr></thead>
                <tbody>
                  {data.topOverdueDepartments.map((d) => (
                    <tr key={d.departmentId}><td>{d.departmentName}</td><td><span className="badge badge-red">{d.overdueCount}</span></td></tr>
                  ))}
                  {!detailsLoading && data.topOverdueDepartments.length === 0 && <tr><td colSpan={2} className="text-center">لا يوجد</td></tr>}
                </tbody>
              </table>
            </div>

            <div className="card">
              <h3>أكثر الجهات وارد منها</h3>
              <table className="data-table">
                <thead><tr><th>الجهة</th><th>العدد</th></tr></thead>
                <tbody>
                  {data.topIncomingParties.map((p, i) => (
                    <tr key={i}><td>{p.partyName}</td><td>{p.transactionCount}</td></tr>
                  ))}
                  {!detailsLoading && data.topIncomingParties.length === 0 && <tr><td colSpan={2} className="text-center">لا يوجد</td></tr>}
                </tbody>
              </table>
            </div>

            <div className="card">
              <h3>حسب التصنيف</h3>
              <table className="data-table">
                <thead><tr><th>التصنيف</th><th>العدد</th></tr></thead>
                <tbody>
                  {data.byCategory.map((c, i) => (
                    <tr key={i}><td>{c.categoryName}</td><td>{c.count}</td></tr>
                  ))}
                  {!detailsLoading && data.byCategory.length === 0 && <tr><td colSpan={2} className="text-center">لا يوجد</td></tr>}
                </tbody>
              </table>
            </div>

            <div className="card">
              <h3>حسب الحالة</h3>
              <table className="data-table">
                <thead><tr><th>الحالة</th><th>العدد</th></tr></thead>
                <tbody>
                  {data.byStatus.map((s, i) => (
                    <tr key={i}><td>{statusLabels[s.status] || s.status}</td><td>{s.count}</td></tr>
                  ))}
                  {!detailsLoading && data.byStatus.length === 0 && <tr><td colSpan={2} className="text-center">لا يوجد</td></tr>}
                </tbody>
              </table>
            </div>
          </div>

          <div className="card mt-4">
            <h3>آخر المعاملات التي تحتاج إجراء</h3>
            <table className="data-table">
              <thead><tr><th>رقم الوارد</th><th>الموضوع</th><th>الإدارة</th><th>الحالة</th><th>التاريخ</th><th>عرض</th></tr></thead>
              <tbody>
                {data.actionRequired.map((t) => (
                  <tr key={t.id} className={t.isOverdue ? 'row-overdue' : ''}>
                    <td>{t.incomingNumber}</td>
                    <td>{t.subject}</td>
                    <td><DepartmentBadges names={t.outgoingDepartmentNames} /></td>
                    <td><span className={`badge ${statusBadgeClass(t.status, t.isOverdue)}`}>{statusLabels[t.status]}</span></td>
                    <td><DateDisplay date={t.incomingDate} /></td>
                    <td><Link to={`/transactions/${t.id}`} className="btn btn-sm">عرض</Link></td>
                  </tr>
                ))}
                {!detailsLoading && data.actionRequired.length === 0 && <tr><td colSpan={6} className="text-center">لا توجد</td></tr>}
              </tbody>
            </table>
          </div>
        </>
      )}
    </div>
  );
}
