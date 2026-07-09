import { useCallback, useEffect, useMemo, useState } from 'react';
import type { FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { dataQualityApi } from '../api/services';
import type { DataQualityIssue, DataQualitySummary } from '../api/types';
import { EmptyState, PageHeader, TableSkeleton } from '../components/ui';

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

const defaultParams: DataQualityParams = { limit: 500 };

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
  const [appliedParams, setAppliedParams] = useState<DataQualityParams>(defaultParams);
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
        await fetchSummary(defaultParams);
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

  const resetFilters = async () => {
    setFrom('');
    setTo('');
    setSeverity('');
    setCategory('');
    setLimit('500');
    setOverdueMoreThanDays('');
    setIncludeReferralDateAfterIncomingDate(false);
    setResponsePeriodLessThanDays('');
    setReviewFilter('unreviewed');
    setAppliedParams(defaultParams);
    await fetchSummary(defaultParams);
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
  const hasAppliedRule = Boolean(
    appliedParams.overdueMoreThanDays !== undefined ||
    appliedParams.includeReferralDateAfterIncomingDate ||
    appliedParams.responsePeriodLessThanDays !== undefined,
  );

  return (
    <div className="page page-data-quality">
      <section className="dq-hero">
        <PageHeader title="جودة البيانات" />
        <p className="dq-page-description">
          تعرض هذه الشاشة المعاملات والملاحظات المطابقة لقواعد جودة البيانات المختارة، مع إمكانية فتح المعاملة أو تعليم الملاحظة كمراجعة دون تعديل بيانات المعاملة من هنا.
        </p>
      </section>

      {message && <output className="alert alert-success dq-alert" aria-live="polite">{message}</output>}
      {error && <div className="alert alert-error dq-alert" role="alert">{error}</div>}

      <DataQualitySummaryCards summary={summary} generatedAt={generatedAt} />

      <DataQualityFilters
        category={category}
        from={from}
        includeReferralDateAfterIncomingDate={includeReferralDateAfterIncomingDate}
        limit={limit}
        loading={loading}
        overdueMoreThanDays={overdueMoreThanDays}
        responsePeriodLessThanDays={responsePeriodLessThanDays}
        reviewFilter={reviewFilter}
        severity={severity}
        to={to}
        onApply={applyFilters}
        onReset={() => {
          void resetFilters();
        }}
        onCategoryChange={setCategory}
        onFromChange={setFrom}
        onIncludeReferralDateAfterIncomingDateChange={setIncludeReferralDateAfterIncomingDate}
        onLimitChange={setLimit}
        onOverdueMoreThanDaysChange={setOverdueMoreThanDays}
        onResponsePeriodLessThanDaysChange={setResponsePeriodLessThanDays}
        onReviewFilterChange={setReviewFilter}
        onSeverityChange={setSeverity}
        onToChange={setTo}
      />

      <DataQualityIssuesTable
        hasAppliedRule={hasAppliedRule}
        issues={summary.issues}
        loading={loading}
        totalIssues={summary.totalIssues}
        onMarkReviewed={(issue) => {
          void markReviewed(issue);
        }}
        onOpenTransaction={(transactionId) => navigate(`/transactions/${transactionId}`)}
        onUnmarkReviewed={(issue) => {
          void unmarkReviewed(issue);
        }}
      />
    </div>
  );
}

type DataQualitySummaryCardsProps = Readonly<{
  generatedAt: string;
  summary: DataQualitySummary;
}>;

function DataQualitySummaryCards({ generatedAt, summary }: DataQualitySummaryCardsProps) {
  return (
    <section className="dq-section" aria-labelledby="dq-summary-heading">
      <div className="dq-section-header">
        <div>
          <h3 id="dq-summary-heading">الملخص</h3>
          <p>نظرة سريعة على حجم الملاحظات حسب مستوى الخطورة وعدد المعاملات المتأثرة.</p>
        </div>
        <span className="badge badge-gray">آخر فحص: {generatedAt}</span>
      </div>
      <div className="stats-grid dq-summary-grid" aria-label="ملخص جودة البيانات">
        <SummaryCard tone="primary" label="إجمالي الملاحظات" value={summary.totalIssues} description="بعد تطبيق الفلاتر" />
        <SummaryCard tone="red" label="حرجة" value={summary.criticalCount} />
        <SummaryCard tone="orange" label="عالية" value={summary.highCount} />
        <SummaryCard tone="yellow" label="متوسطة" value={summary.mediumCount} />
        <SummaryCard tone="gray" label="منخفضة" value={summary.lowCount} />
        <SummaryCard tone="blue" label="معاملات متأثرة" value={summary.affectedTransactions} description="معاملات لديها ملاحظة واحدة أو أكثر" />
      </div>
    </section>
  );
}

function SummaryCard({
  description,
  label,
  tone,
  value,
}: Readonly<{ description?: string; label: string; tone: string; value: number | string }>) {
  return (
    <article className={`stat-card dq-stat-card dq-stat-card-${tone}`}>
      <div className="stat-label">{label}</div>
      <div className="stat-value">{value}</div>
      {description && <div className="dq-stat-description">{description}</div>}
    </article>
  );
}

type DataQualityFiltersProps = Readonly<{
  category: string;
  from: string;
  includeReferralDateAfterIncomingDate: boolean;
  limit: string;
  loading: boolean;
  overdueMoreThanDays: string;
  responsePeriodLessThanDays: string;
  reviewFilter: ReviewFilter;
  severity: string;
  to: string;
  onApply: (event: FormEvent) => Promise<void>;
  onCategoryChange: (value: string) => void;
  onFromChange: (value: string) => void;
  onIncludeReferralDateAfterIncomingDateChange: (value: boolean) => void;
  onLimitChange: (value: string) => void;
  onOverdueMoreThanDaysChange: (value: string) => void;
  onReset: () => void;
  onResponsePeriodLessThanDaysChange: (value: string) => void;
  onReviewFilterChange: (value: ReviewFilter) => void;
  onSeverityChange: (value: string) => void;
  onToChange: (value: string) => void;
}>;

function DataQualityFilters({
  category,
  from,
  includeReferralDateAfterIncomingDate,
  limit,
  loading,
  overdueMoreThanDays,
  responsePeriodLessThanDays,
  reviewFilter,
  severity,
  to,
  onApply,
  onCategoryChange,
  onFromChange,
  onIncludeReferralDateAfterIncomingDateChange,
  onLimitChange,
  onOverdueMoreThanDaysChange,
  onReset,
  onResponsePeriodLessThanDaysChange,
  onReviewFilterChange,
  onSeverityChange,
  onToChange,
}: DataQualityFiltersProps) {
  return (
    <section className="dq-section dq-filter-card" aria-labelledby="dq-filter-heading">
      <div className="dq-section-header">
        <div>
          <h3 id="dq-filter-heading">الفلاتر وقواعد الاكتشاف</h3>
          <p>اختر قاعدة واحدة أو أكثر، ثم طبّق الفلاتر لعرض الحالات التي تحتاج مراجعة.</p>
        </div>
      </div>
      <form className="dq-filters-form" onSubmit={onApply}>
        <div className="dq-filter-grid">
          <label className="dq-field">
            <span>من تاريخ</span>
            <input type="date" value={from} onChange={(event) => onFromChange(event.target.value)} />
          </label>
          <label className="dq-field">
            <span>إلى تاريخ</span>
            <input type="date" value={to} onChange={(event) => onToChange(event.target.value)} />
          </label>
          <label className="dq-field">
            <span>الخطورة</span>
            <select value={severity} onChange={(event) => onSeverityChange(event.target.value)}>
              <option value="">الكل</option>
              <option value="Critical">حرجة</option>
              <option value="High">عالية</option>
              <option value="Medium">متوسطة</option>
              <option value="Low">منخفضة</option>
            </select>
          </label>
          <label className="dq-field">
            <span>التصنيف</span>
            <select value={category} onChange={(event) => onCategoryChange(event.target.value)}>
              <option value="">الكل</option>
              <option value="التأخر">التأخر</option>
              <option value="الإحالات">الإحالات</option>
              <option value="فترة الرد">فترة الرد</option>
            </select>
          </label>
          <label className="dq-field">
            <span>حالة المراجعة</span>
            <select value={reviewFilter} onChange={(event) => onReviewFilterChange(event.target.value as ReviewFilter)}>
              <option value="unreviewed">إخفاء المراجع افتراضيًا</option>
              <option value="all">عرض الكل</option>
              <option value="reviewed">المراجعة فقط</option>
            </select>
          </label>
          <label className="dq-field">
            <span>الحد الأقصى للنتائج</span>
            <input min="1" max="1000" type="number" value={limit} onChange={(event) => onLimitChange(event.target.value)} />
          </label>
        </div>

        <div className="dq-rules-grid" aria-label="قواعد جودة البيانات">
          <label className={`dq-rule-card ${overdueMoreThanDays !== '' ? 'dq-rule-card-active' : ''}`}>
            <span className="dq-rule-title">متأخر أكثر من X يوم</span>
            <span className="dq-rule-description">يعرض المعاملات المفتوحة التي تجاوزت مدة التأخر التي تحددها.</span>
            <input
              min="0"
              type="number"
              value={overdueMoreThanDays}
              onChange={(event) => onOverdueMoreThanDaysChange(event.target.value)}
              placeholder="عدد الأيام"
            />
          </label>
          <label className={`dq-rule-card dq-rule-card-checkbox ${includeReferralDateAfterIncomingDate ? 'dq-rule-card-active' : ''}`}>
            <input
              aria-label="عرض المعاملات التي تاريخ الإحالة فيها أكبر من تاريخ الوارد"
              className="dq-rule-checkbox-control"
              type="checkbox"
              checked={includeReferralDateAfterIncomingDate}
              onChange={(event) => onIncludeReferralDateAfterIncomingDateChange(event.target.checked)}
            />
            <span className="dq-rule-title">تاريخ الإحالة أكبر من تاريخ الوارد</span>
            <span className="dq-rule-description">حالة تحتاج مراجعة؛ لا تعني وجود خطأ قطعي دائمًا.</span>
          </label>
          <label className={`dq-rule-card ${responsePeriodLessThanDays !== '' ? 'dq-rule-card-active' : ''}`}>
            <span className="dq-rule-title">فترة الرد أقل من X يوم</span>
            <span className="dq-rule-description">يعرض المعاملات التي حُددت لها فترة رد أقصر من الحد المختار.</span>
            <input
              min="0"
              type="number"
              value={responsePeriodLessThanDays}
              onChange={(event) => onResponsePeriodLessThanDaysChange(event.target.value)}
              placeholder="عدد الأيام"
            />
          </label>
        </div>

        <div className="dq-filter-actions">
          <button type="submit" className="btn btn-primary" disabled={loading}>
            تطبيق الفلاتر
          </button>
          <button type="button" className="btn btn-outline" disabled={loading} onClick={onReset}>
            إعادة تعيين
          </button>
        </div>
      </form>
    </section>
  );
}

type DataQualityIssuesTableProps = Readonly<{
  hasAppliedRule: boolean;
  issues: DataQualityIssue[];
  loading: boolean;
  totalIssues: number;
  onMarkReviewed: (issue: DataQualityIssue) => void;
  onOpenTransaction: (transactionId: number) => void;
  onUnmarkReviewed: (issue: DataQualityIssue) => void;
}>;

function DataQualityIssuesTable({
  hasAppliedRule,
  issues,
  loading,
  totalIssues,
  onMarkReviewed,
  onOpenTransaction,
  onUnmarkReviewed,
}: DataQualityIssuesTableProps) {
  const resultsContent = renderDataQualityIssuesContent({
    hasAppliedRule,
    issues,
    loading,
    onMarkReviewed,
    onOpenTransaction,
    onUnmarkReviewed,
  });

  return (
    <section className="dq-section dq-results-section" aria-labelledby="dq-results-heading">
      <div className="dq-section-header">
        <div>
          <h3 id="dq-results-heading">النتائج</h3>
          <p>{issues.length} نتيجة معروضة من أصل {totalIssues} ملاحظة مطابقة.</p>
        </div>
      </div>

      {resultsContent}
    </section>
  );
}

type DataQualityIssuesContentProps = Omit<DataQualityIssuesTableProps, 'totalIssues'>;

function renderDataQualityIssuesContent({
  hasAppliedRule,
  issues,
  loading,
  onMarkReviewed,
  onOpenTransaction,
  onUnmarkReviewed,
}: DataQualityIssuesContentProps) {
  if (loading) {
    return <TableSkeleton rows={5} cols={8} />;
  }

  if (!hasAppliedRule) {
    return (
      <EmptyState
        icon="🔎"
        title="اختر قاعدة جودة بيانات للبدء"
        description="حدد مدة التأخر أو فعّل قاعدة تاريخ الإحالة أو أدخل حد فترة الرد، ثم اضغط تطبيق الفلاتر."
      />
    );
  }

  if (issues.length === 0) {
    return (
      <EmptyState
        icon="✅"
        title="لا توجد ملاحظات مطابقة للفلاتر المحددة."
        description="يمكنك تعديل القواعد أو حالة المراجعة لتوسيع نطاق البحث."
      />
    );
  }

  return (
    <div className="table-wrapper dq-table-wrapper">
      <table className="data-table dq-issues-table">
        <thead>
          <tr>
            <th>الخطورة</th>
            <th>الملاحظة</th>
            <th>المعاملة</th>
            <th>الإدارة</th>
            <th>القيمة</th>
            <th>الأثر والإجراء</th>
            <th>المراجعة</th>
            <th>إجراءات</th>
          </tr>
        </thead>
        <tbody>
          {issues.map((issue) => (
            <DataQualityIssueRow
              key={issue.id}
              issue={issue}
              onMarkReviewed={onMarkReviewed}
              onOpenTransaction={onOpenTransaction}
              onUnmarkReviewed={onUnmarkReviewed}
            />
          ))}
        </tbody>
      </table>
    </div>
  );
}

type DataQualityIssueRowProps = Readonly<{
  issue: DataQualityIssue;
  onMarkReviewed: (issue: DataQualityIssue) => void;
  onOpenTransaction: (transactionId: number) => void;
  onUnmarkReviewed: (issue: DataQualityIssue) => void;
}>;

function DataQualityIssueRow({
  issue,
  onMarkReviewed,
  onOpenTransaction,
  onUnmarkReviewed,
}: DataQualityIssueRowProps) {
  const transactionDisplayValue = getIssueTransactionDisplayValue(issue);
  const transactionId = issue.transactionId;

  return (
    <tr>
      <td className="dq-issues-table-cell">
        <span className={`badge ${severityBadgeClass(issue.severity)}`}>{issue.severityLabel}</span>
      </td>
      <td className="dq-issues-table-cell">
        <div className="dq-issue-cell">
          <span className="badge badge-blue">{issue.category}</span>
          <strong>{issue.issueType}</strong>
          <span className="dq-muted">{issue.fieldName}</span>
        </div>
      </td>
      <td className="dq-issues-table-cell">
        <div className="dq-transaction-cell">
          <strong>{transactionDisplayValue}</strong>
          <span>الوارد: {issue.incomingNumber ?? '—'}</span>
          <span className="dq-subject">{issue.subject ?? '—'}</span>
        </div>
      </td>
      <td className="dq-issues-table-cell">{issue.departmentName ? <span className="badge badge-gray">{issue.departmentName}</span> : '—'}</td>
      <td className="dq-issues-table-cell">
        <div className="dq-value-cell">
          <span>{issue.currentValue ?? '—'}</span>
          {issue.daysValue !== undefined && issue.daysValue !== null && (
            <span className="badge badge-purple">{issue.daysValue} يوم</span>
          )}
        </div>
      </td>
      <td className="dq-issues-table-cell">
        <div className="dq-impact-cell">
          <span>{issue.impact}</span>
          <strong>{issue.suggestedAction}</strong>
        </div>
      </td>
      <td className="dq-issues-table-cell">
        <span className={`badge ${issue.isReviewed ? 'badge-green' : 'badge-yellow'}`}>
          {issue.isReviewed ? 'مراجع' : 'غير مراجع'}
        </span>
      </td>
      <td className="dq-issues-table-cell">
        <div className="dq-row-actions">
          {transactionId && (
            <button type="button" className="btn btn-secondary btn-sm" onClick={() => onOpenTransaction(transactionId)}>
              فتح المعاملة
            </button>
          )}
          {!issue.isReviewed ? (
            <button type="button" className="btn btn-outline btn-sm" onClick={() => onMarkReviewed(issue)}>
              تعليم كمراجعة
            </button>
          ) : (
            <button type="button" className="btn btn-outline btn-sm" onClick={() => onUnmarkReviewed(issue)}>
              إزالة المراجعة
            </button>
          )}
        </div>
      </td>
    </tr>
  );
}

function getIssueTransactionDisplayValue(issue: DataQualityIssue): string {
  if (issue.trackingNumber) {
    return issue.trackingNumber;
  }

  if (issue.transactionId) {
    return String(issue.transactionId);
  }

  return '—';
}

function severityBadgeClass(severity: number): string {
  if (severity >= 4) return 'badge-red';
  if (severity === 3) return 'badge-orange';
  if (severity === 2) return 'badge-yellow';
  return 'badge-gray';
}
