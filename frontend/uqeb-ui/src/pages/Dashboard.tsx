import { useEffect, useState } from 'react';
import { Link } from 'react-router-dom';
import { dashboardApi } from '../api/services';
import type { DashboardSummary } from '../api/types';
import { statusLabels, statusBadgeClass } from '../utils/labels';
import DateDisplay from '../components/DateDisplay';
import DepartmentBadges from '../components/DepartmentBadges';

export default function DashboardPage() {
  const [data, setData] = useState<DashboardSummary | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    dashboardApi.summary().then((res) => setData(res.data)).finally(() => setLoading(false));
  }, []);

  if (loading) return <div className="loading">جاري التحميل...</div>;
  if (!data) return <div className="alert alert-error">فشل تحميل البيانات</div>;

  const cards = [
    { label: 'معاملات مفتوحة', value: data.totalOpen, color: 'blue', link: '/transactions' },
    { label: 'مطلوب إفادة', value: data.requiresResponsePending, color: 'purple', link: '/reports' },
    { label: 'متأخر في الإفادة', value: data.responseOverdueCount, color: 'red', link: '/reports' },
    { label: 'بانتظار رد', value: data.waitingForReply, color: 'orange', link: '/reports' },
    { label: 'رد جزئي', value: data.partiallyReplied, color: 'cyan', link: '/reports' },
    { label: 'جاهزة للإفادة', value: data.readyForResponse, color: 'green', link: '/transactions?status=ReadyForResponse' },
    { label: 'مغلقة هذا الشهر', value: data.closedThisMonth, color: 'gray', link: '/transactions?status=Closed' },
    { label: 'متوسط أيام الإنجاز', value: data.averageCompletionDays, color: 'blue', link: '/reports', suffix: ' يوم' },
  ];

  return (
    <div>
      <h2 className="page-title">لوحة المتابعة</h2>

      <div className="stats-grid">
        {cards.map((c) => (
          <Link key={c.label} to={c.link} className={`stat-card stat-${c.color}`}>
            <div className="stat-value">{c.value}{c.suffix || ''}</div>
            <div className="stat-label">{c.label}</div>
          </Link>
        ))}
      </div>

      <div className="dashboard-grid mt-4">
        <div className="card">
          <h3>أكثر الإدارات تأخراً</h3>
          <table className="data-table">
            <thead><tr><th>الإدارة</th><th>المتأخر</th></tr></thead>
            <tbody>
              {data.topOverdueDepartments.map((d) => (
                <tr key={d.departmentId}><td>{d.departmentName}</td><td><span className="badge badge-red">{d.overdueCount}</span></td></tr>
              ))}
              {data.topOverdueDepartments.length === 0 && <tr><td colSpan={2} className="text-center">لا يوجد</td></tr>}
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
            {data.actionRequired.length === 0 && <tr><td colSpan={6} className="text-center">لا توجد</td></tr>}
          </tbody>
        </table>
      </div>
    </div>
  );
}
