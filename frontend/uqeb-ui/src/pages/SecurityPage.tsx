import { useCallback, useEffect, useState } from 'react';
import { securityApi } from '../api/services';
import type { LoginAttemptLog, SecurityAlert } from '../api/types';

const alertTypeLabels: Record<string, string> = {
  account_bruteforce: 'تخمين كلمة مرور (حساب)',
  failed_login_burst: 'ارتفاع محاولات فاشلة',
  ip_password_spray: 'تخمين أسماء مستخدمين',
  unauthorized_access: 'وصول غير مصرح',
  admin_endpoint_probe: 'محاولة مسار إداري',
};

const severityLabels: Record<string, string> = {
  low: 'منخفض',
  medium: 'متوسط',
  high: 'عالٍ',
  critical: 'حرج',
};

function severityBadge(severity: string) {
  const map: Record<string, string> = {
    low: 'badge-gray',
    medium: 'badge-yellow',
    high: 'badge-orange',
    critical: 'badge-red',
  };
  return map[severity] || 'badge-gray';
}

function formatDate(value: string) {
  try {
    return new Date(value).toLocaleString('ar-SA');
  } catch {
    return value;
  }
}

export default function SecurityPage() {
  const [unreadCount, setUnreadCount] = useState(0);
  const [alerts, setAlerts] = useState<SecurityAlert[]>([]);
  const [attempts, setAttempts] = useState<LoginAttemptLog[]>([]);
  const [loading, setLoading] = useState(true);
  const [alertSeverity, setAlertSeverity] = useState('');
  const [alertType, setAlertType] = useState('');
  const [attemptSucceeded, setAttemptSucceeded] = useState('');
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [alertsRes, attemptsRes] = await Promise.all([
        securityApi.getAlerts({
          severity: alertSeverity || undefined,
          type: alertType || undefined,
          pageSize: 100,
        }),
        securityApi.getLoginAttempts({
          succeeded: attemptSucceeded === '' ? undefined : attemptSucceeded === 'true',
          pageSize: 100,
        }),
      ]);
      setUnreadCount(alertsRes.data.unreadCount);
      setAlerts(alertsRes.data.items);
      setAttempts(attemptsRes.data.items);
    } catch {
      setError('تعذر تحميل بيانات الأمن والتنبيهات');
    } finally {
      setLoading(false);
    }
  }, [alertSeverity, alertType, attemptSucceeded]);

  useEffect(() => { load(); }, [load]);

  const markRead = async (id: number) => {
    try {
      await securityApi.markAlertRead(id);
      await load();
    } catch {
      setError('تعذر تحميل بيانات الأمن والتنبيهات');
    }
  };

  const markAllRead = async () => {
    try {
      await securityApi.markAllAlertsRead();
      await load();
    } catch {
      setError('تعذر تحميل بيانات الأمن والتنبيهات');
    }
  };

  return (
    <div>
      <div className="page-header">
        <h2 className="page-title">الأمن والتنبيهات</h2>
        <div style={{ display: 'flex', gap: '0.5rem', alignItems: 'center' }}>
          {unreadCount > 0 && (
            <span className="badge badge-red">{unreadCount} تنبيه غير مقروء</span>
          )}
          <button className="btn btn-outline" onClick={markAllRead} disabled={unreadCount === 0}>
            تعليم الكل كمقروء
          </button>
          <button className="btn btn-outline" onClick={load}>تحديث</button>
        </div>
      </div>

      {error && <div className="alert alert-error">{error}</div>}
      {loading && <p>جاري التحميل...</p>}

      <section style={{ marginBottom: '2rem' }}>
        <h3>التنبيهات الأمنية</h3>
        <div className="filters-row" style={{ display: 'flex', gap: '1rem', marginBottom: '1rem', flexWrap: 'wrap' }}>
          <div className="form-group" style={{ margin: 0 }}>
            <label>الخطورة</label>
            <select value={alertSeverity} onChange={(e) => setAlertSeverity(e.target.value)}>
              <option value="">الكل</option>
              {Object.entries(severityLabels).map(([k, v]) => (
                <option key={k} value={k}>{v}</option>
              ))}
            </select>
          </div>
          <div className="form-group" style={{ margin: 0 }}>
            <label>النوع</label>
            <select value={alertType} onChange={(e) => setAlertType(e.target.value)}>
              <option value="">الكل</option>
              {Object.entries(alertTypeLabels).map(([k, v]) => (
                <option key={k} value={k}>{v}</option>
              ))}
            </select>
          </div>
        </div>
        <table className="data-table">
          <thead>
            <tr>
              <th>الوقت</th>
              <th>النوع</th>
              <th>العنوان</th>
              <th>الرسالة</th>
              <th>الخطورة</th>
              <th>المستخدم</th>
              <th>IP</th>
              <th>الحالة</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {alerts.length === 0 && !loading && (
              <tr><td colSpan={9} style={{ textAlign: 'center' }}>لا توجد تنبيهات</td></tr>
            )}
            {alerts.map((a) => (
              <tr key={a.id} style={a.isRead ? undefined : { background: '#fffbeb' }}>
                <td>{formatDate(a.createdAt)}</td>
                <td>{alertTypeLabels[a.type] || a.type}</td>
                <td>{a.title}</td>
                <td>{a.message}</td>
                <td><span className={`badge ${severityBadge(a.severity)}`}>{severityLabels[a.severity] || a.severity}</span></td>
                <td>{a.username || '-'}</td>
                <td>{a.ipAddress || '-'}</td>
                <td>{a.isRead ? 'مقروء' : 'جديد'}</td>
                <td>
                  {!a.isRead && (
                    <button className="btn btn-outline btn-sm" onClick={() => markRead(a.id)}>تعليم كمقروء</button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </section>

      <section>
        <h3>آخر محاولات تسجيل الدخول</h3>
        <div className="filters-row" style={{ display: 'flex', gap: '1rem', marginBottom: '1rem' }}>
          <div className="form-group" style={{ margin: 0 }}>
            <label>النتيجة</label>
            <select value={attemptSucceeded} onChange={(e) => setAttemptSucceeded(e.target.value)}>
              <option value="">الكل</option>
              <option value="true">ناجحة</option>
              <option value="false">فاشلة</option>
            </select>
          </div>
        </div>
        <table className="data-table">
          <thead>
            <tr>
              <th>الوقت</th>
              <th>المستخدم</th>
              <th>IP</th>
              <th>النتيجة</th>
              <th>سبب الفشل</th>
              <th>مستوى المخاطر</th>
              <th>User Agent</th>
            </tr>
          </thead>
          <tbody>
            {attempts.length === 0 && !loading && (
              <tr><td colSpan={7} style={{ textAlign: 'center' }}>لا توجد محاولات</td></tr>
            )}
            {attempts.map((l) => (
              <tr key={l.id}>
                <td>{formatDate(l.occurredAt)}</td>
                <td>{l.username || '-'}</td>
                <td>{l.ipAddress || '-'}</td>
                <td>
                  <span className={`badge ${l.succeeded ? 'badge-green' : 'badge-red'}`}>
                    {l.succeeded ? 'ناجحة' : 'فاشلة'}
                  </span>
                </td>
                <td>{l.failureReason || '-'}</td>
                <td>{severityLabels[l.riskLevel] || l.riskLevel}</td>
                <td style={{ maxWidth: 200, overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }} title={l.userAgent || ''}>
                  {l.userAgent || '-'}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </section>
    </div>
  );
}
