import { useCallback, useEffect, useMemo, useState } from 'react';
import type { FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { dataQualityApi } from '../api/services';
import type { DataQualityIssue, DataQualitySummary } from '../api/types';
import { EmptyState, PageHeader } from '../components/ui';

type ReviewFilter = 'unreviewed' | 'all' | 'reviewed';
type DataQualityParams = Record<string, string | number | boolean>;

const emptySummary: DataQualitySummary = {
  totalIssues: 0,
  criticalCount: 0,
  highCount: 0,
  mediumCount: 0,
  lowCount: 0,
  affectedTransactions: 0,
  generatedAtUtc: '',
  issues: [],
};

export default function DataQualityPage() {
  const navigate = useNavigate();
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');
  const [severity, setSeverity] = useState('');
  const [category, setCategory] = useState('');
  const [limit, setLimit] = useState('500');
  const [overdueMoreThanDays, setOverdueMoreThanDays] = useState('');
  const [includeReferralDateAfterIncomingDate, setIncludeReferralDateAfterIncomingDate] = useState(false);
  const [responsePeriodLessThanDays, setResponsePeriodLessThanDays] = useState('');
  const [reviewFilter, setReviewFilter] = useState<ReviewFilter>('unreviewed');
  const [appliedParams, setAppliedParams] = useState<DataQualityParams>({ limit: 500 });
  const [summary, setSummary] = useState<DataQualitySummary>(emptySummary);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState('');
  const [message, setMessage] = useState('');

  const params = useMemo(() => {
    const next: DataQualityParams = {};
    if (from) next.from = from;
    if (to) next.to = to;
    if (severity) next.severity = severity;
    if (category) next.category = category;
    if (limit) next.limit = Number(limit);
    if (overdueMoreThanDays !== '') next.overdueMoreThanDays = Number(overdueMoreThanDays);
    if (includeReferralDateAfterIncomingDate) next.includeReferralDateAfterIncomingDate = true;
    if (responsePeriodLessThanDays !== '') next.responsePeriodLessThanDays = Number(responsePeriodLessThanDays);
    if (reviewFilter === 'all') next.includeReviewed = true;
    if (reviewFilter === 'reviewed') next.reviewedOnly = true;
    return next;
  }, [
    category,
    from,
    includeReferralDateAfterIncomingDate,
    limit,
    overdueMoreThanDays,
    responsePeriodLessThanDays,
    reviewFilter,
    severity,
    to,
  ]);

  const fetchSummary = useCallback(async (nextParams: DataQualityParams) => {
    setLoading(true);
    setError('');
    try {
      const { data } = await dataQualityApi.getSummary(nextParams);
      setSummary(data);
    } catch {
      setError('تعذر تحميل ملاحظات جودة البيانات.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    const timeoutId = window.setTimeout(() => {
      const loadInitialSummary = async () => {
        await fetchSummary({ limit: 500 });
      };

      loadInitialSummary().catch(() => {
        setError('تعذر تحميل ملاحظات جودة البيانات.');
      });
    }, 0);
    return () => window.clearTimeout(timeoutId);
  }, [fetchSummary]);

  const applyFilters = async (event: FormEvent) => {
    event.preventDefault();
    setAppliedParams(params);
    await fetchSummary(params);
  };

  const markReviewed = async (issue: DataQualityIssue) => {
    try {
      await dataQualityApi.markReviewed({
        issueKey: issue.issueKey,
        transactionId: issue.transactionId,
        ruleCode: issue.ruleCode,
      });
      setError('');
      setMessage('تمت مراجعة هذه الملاحظة، ولن تظهر في النتائج الافتراضية القادمة.');
      await fetchSummary(appliedParams);
    } catch {
      setMessage('');
      setError('تعذر تعليم الملاحظة كمراجعة.');
    }
  };

  const unmarkReviewed = async (issue: DataQualityIssue) => {
    try {
      await dataQualityApi.unmarkReviewed({ issueKey: issue.issueKey });
      setError('');
      setMessage('تمت إزالة علامة المراجعة، وستعود الملاحظة للظهور إذا بقيت القاعدة منطبقة.');
      await fetchSummary(appliedParams);
    } catch {
      setMessage('');
      setError('تعذر إزالة علامة المراجعة.');
    }
  };

  const generatedAt = summary.generatedAtUtc
    ? new Date(summary.generatedAtUtc).toLocaleString('ar-SA')
    : 'غير متاح';

  return (
    <div className="page page-data-quality">
      <PageHeader title="جودة البيانات" />

      <div className="alert alert-info">
        هذه الشاشة تعرض الحالات المطابقة للقواعد المختارة فقط. التعديل يتم من صفحة تفاصيل المعاملة بعد فتحها.
      </div>
      <div className="alert alert-warning">
        تاريخ الإحالة الأكبر من تاريخ الوارد لا يعني خطأ دائمًا، لكنه يعرض الحالات التي تحتاج مراجعة لأن الغالب أن تاريخ الإحالة يساوي تاريخ الوارد.
      </div>

      <form className="filters-panel" onSubmit={applyFilters}>
        <label>
          <span>من تاريخ</span>
          <input type="date" value={from} onChange={(event) => setFrom(event.target.value)} />
        </label>
        <label>
          <span>إلى تاريخ</span>
          <input type="date" value={to} onChange={(event) => setTo(event.target.value)} />
        </label>
        <label>
          <span>الخطورة</span>
          <select value={severity} onChange={(event) => setSeverity(event.target.value)}>
            <option value="">الكل</option>
            <option value="Critical">حرجة</option>
            <option value="High">عالية</option>
            <option value="Medium">متوسطة</option>
            <option value="Low">منخفضة</option>
          </select>
        </label>
        <label>
          <span>التصنيف</span>
          <select value={category} onChange={(event) => setCategory(event.target.value)}>
            <option value="">الكل</option>
            <option value="التأخر">التأخر</option>
            <option value="الإحالات">الإحالات</option>
            <option value="فترة الرد">فترة الرد</option>
          </select>
        </label>
        <label>
          <span>الحد الأقصى للنتائج</span>
          <input min="1" max="1000" type="number" value={limit} onChange={(event) => setLimit(event.target.value)} />
        </label>
        <label>
          <span>عرض المعاملات المتأخرة أكثر من</span>
          <input
            min="0"
            type="number"
            value={overdueMoreThanDays}
            onChange={(event) => setOverdueMoreThanDays(event.target.value)}
            placeholder="عدد الأيام"
          />
        </label>
        <label className="checkbox-row">
          <input
            type="checkbox"
            checked={includeReferralDateAfterIncomingDate}
            onChange={(event) => setIncludeReferralDateAfterIncomingDate(event.target.checked)}
          />
          <span>عرض المعاملات التي تاريخ الإحالة فيها أكبر من تاريخ الوارد</span>
        </label>
        <label>
          <span>عرض المعاملات التي فترة الرد المحددة لها أقل من</span>
          <input
            min="0"
            type="number"
            value={responsePeriodLessThanDays}
            onChange={(event) => setResponsePeriodLessThanDays(event.target.value)}
            placeholder="عدد الأيام"
          />
        </label>
        <label>
          <span>حالة المراجعة</span>
          <select value={reviewFilter} onChange={(event) => setReviewFilter(event.target.value as ReviewFilter)}>
            <option value="unreviewed">غير مراجعة فقط</option>
            <option value="all">الكل</option>
            <option value="reviewed">مراجعة فقط</option>
          </select>
        </label>
        <button type="submit" disabled={loading}>تطبيق الفلاتر</button>
      </form>

      {message && <div className="alert alert-success">{message}</div>}
      {error && <div className="alert alert-danger">{error}</div>}

      <section className="stats-grid" aria-label="ملخص جودة البيانات">
        <SummaryCard label="إجمالي الملاحظات" value={summary.totalIssues} />
        <SummaryCard label="حرجة" value={summary.criticalCount} />
        <SummaryCard label="عالية" value={summary.highCount} />
        <SummaryCard label="متوسطة" value={summary.mediumCount} />
        <SummaryCard label="منخفضة" value={summary.lowCount} />
        <SummaryCard label="معاملات متأثرة" value={summary.affectedTransactions} />
        <SummaryCard label="آخر فحص" value={generatedAt} />
      </section>

      {loading && <p className="text-muted">جاري تحميل ملاحظات جودة البيانات...</p>}

      {!loading && summary.issues.length === 0 ? (
        <EmptyState title="لا توجد ملاحظات مطابقة للفلاتر المحددة." />
      ) : (
        <div className="table-responsive">
          <table className="data-table">
            <thead>
              <tr>
                <th>الخطورة</th>
                <th>التصنيف</th>
                <th>نوع الملاحظة</th>
                <th>رقم المعاملة/التتبع</th>
                <th>رقم الوارد</th>
                <th>الموضوع</th>
                <th>الإدارة</th>
                <th>الحقل</th>
                <th>القيمة الحالية</th>
                <th>عدد الأيام</th>
                <th>الأثر</th>
                <th>الإجراء المقترح</th>
                <th>حالة المراجعة</th>
                <th>إجراءات</th>
              </tr>
            </thead>
            <tbody>
              {summary.issues.map((issue) => (
                <tr key={issue.id}>
                  <td>{issue.severityLabel}</td>
                  <td>{issue.category}</td>
                  <td>{issue.issueType}</td>
                  <td>{issue.trackingNumber ?? issue.transactionId ?? '—'}</td>
                  <td>{issue.incomingNumber ?? '—'}</td>
                  <td>{issue.subject ?? '—'}</td>
                  <td>{issue.departmentName ?? '—'}</td>
                  <td>{issue.fieldName}</td>
                  <td>{issue.currentValue ?? '—'}</td>
                  <td>{issue.daysValue ?? '—'}</td>
                  <td>{issue.impact}</td>
                  <td>{issue.suggestedAction}</td>
                  <td>{issue.isReviewed ? 'تمت المراجعة' : 'غير مراجعة'}</td>
                  <td>
                    <div className="row-actions">
                      {issue.transactionId && (
                        <button type="button" onClick={() => navigate(`/transactions/${issue.transactionId}`)}>
                          فتح المعاملة
                        </button>
                      )}
                      {!issue.isReviewed ? (
                        <button type="button" onClick={() => void markReviewed(issue)}>
                          تمت المراجعة
                        </button>
                      ) : (
                        <button type="button" onClick={() => void unmarkReviewed(issue)}>
                          إزالة المراجعة
                        </button>
                      )}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}

function SummaryCard({ label, value }: Readonly<{ label: string; value: number | string }>) {
  return (
    <article className="stat-card">
      <div className="stat-label">{label}</div>
      <div className="stat-value">{value}</div>
    </article>
  );
}
