import type { ReactNode } from 'react';
import type { Assignment, TransactionDetail } from '../../api/types';
import DateDisplay from '../DateDisplay';
import { StatusBadge, PriorityBadge } from '../ui';
import DepartmentBadges from '../DepartmentBadges';
import { responseTimingBadgeClass, formatCompletionDays, formatDaysSince } from '../../utils/responseTiming';

type TransactionWorkspaceHeaderProps = Readonly<{
  tx: TransactionDetail;
  assignments: Assignment[];
  attachmentsCount: number;
  actionsSlot: ReactNode;
  children?: ReactNode;
}>;

function formatDaysRemaining(days?: number | null): string {
  if (days === undefined || days === null) return '—';
  if (days < 0) return `متأخر ${Math.abs(days)} يوم`;
  if (days === 0) return 'اليوم';
  return `${days} يوم`;
}

function countOpenAssignments(items: Assignment[]): number {
  return items.filter(
    (item) => item.requiresReply && item.replyStatus !== 'Replied' && item.status !== 'Cancelled',
  ).length;
}

function getCompletionDateHint(hasOfficialCompletionDate: boolean, hasEffectiveCompletionDate: boolean): string {
  if (hasOfficialCompletionDate) return 'تاريخ الإغلاق الرسمي';
  if (hasEffectiveCompletionDate) return 'محسوب من آخر تاريخ إغلاق إحالة مطلوبة';
  return 'يُحسب عند إغلاق جميع الإحالات المطلوبة';
}

function getResponseTimingLabel(isResponseOverdue: boolean, hasEffectiveCompletionDate: boolean): string {
  if (isResponseOverdue) return 'متأخرة';
  if (hasEffectiveCompletionDate) return 'مُنجزة';
  return 'في الوقت';
}

function toUtcDateOnlyTime(value: string): number {
  const date = new Date(value);
  return Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), date.getUTCDate());
}

export default function TransactionWorkspaceHeader({
  tx,
  assignments,
  attachmentsCount,
  actionsSlot,
  children,
}: TransactionWorkspaceHeaderProps) {
  const openAssignmentsCount = countOpenAssignments(assignments);

  // Derive effective close date from assignments when ClosedAt is not set
  const requiredAssignments = assignments.filter((a) => a.requiresReply && a.status !== 'Cancelled');
  const allRequiredHaveResponse = requiredAssignments.length > 0
    && requiredAssignments.every((a) => a.responseDate != null);
  const derivedCompletionDate: string | null = allRequiredHaveResponse
    ? requiredAssignments.reduce<string | null>((max, a) => (!max || a.responseDate! > max ? a.responseDate! : max), null)
    : null;
  const effectiveCompletionDate = tx.completionDate ?? derivedCompletionDate;

  const effectiveCompletionDays = tx.completionDays ?? (
    effectiveCompletionDate && tx.incomingDate
      ? Math.max(0, Math.floor((toUtcDateOnlyTime(effectiveCompletionDate) - toUtcDateOnlyTime(tx.incomingDate)) / 86400000))
      : null
  );
  const hasOfficialCompletionDate = Boolean(tx.completionDate);
  const hasEffectiveCompletionDate = Boolean(effectiveCompletionDate);
  const hasEffectiveCompletionDays = effectiveCompletionDays != null;
  const completionDateHint = getCompletionDateHint(hasOfficialCompletionDate, hasEffectiveCompletionDate);
  const completionDaysLabel = hasEffectiveCompletionDays ? 'أيام إنجاز المعاملة' : 'الأيام المفتوحة';
  const completionDaysValue = hasEffectiveCompletionDays
    ? formatCompletionDays(effectiveCompletionDays)
    : formatDaysSince(tx.daysSinceIncoming, '0');
  const completionDaysHint = hasEffectiveCompletionDays
    ? 'محسوب تلقائيًا: تاريخ الإغلاق − تاريخ الوارد'
    : 'محسوب تلقائيًا: اليوم − تاريخ الوارد';
  const responseTimingLabel = getResponseTimingLabel(tx.isResponseOverdue, hasEffectiveCompletionDate);

  return (
    <section className="card transaction-hero-card" aria-label="معلومات المعاملة">
      <div className="transaction-hero-top">
        <div className="transaction-hero-title-block">
          <div className="transaction-hero-title-row">
            <h2 className="transaction-hero-number">{tx.incomingNumber}</h2>
            <StatusBadge status={tx.status} isOverdue={tx.isOverdue} />
            <PriorityBadge priority={tx.priority} />
            {tx.isOverdue && <span className="badge badge-red">متأخرة</span>}
            {tx.hasPendingAssignments && <span className="badge badge-orange">باقي إجراء</span>}
            {tx.responseTimingLabel && tx.requiresResponse && (
              <span className={`badge badge-spaced ${responseTimingBadgeClass(tx.responseTimingStatus)}`}>
                {tx.responseTimingLabel}
              </span>
            )}
          </div>
          <p className="transaction-hero-subject">{tx.subject}</p>
          <div className="transaction-hero-meta">
            <span>{tx.incomingFrom || '—'}</span>
            <span className="transaction-hero-meta-sep">•</span>
            <span>{tx.categoryName || '—'}</span>
            <span className="transaction-hero-meta-sep">•</span>
            <DepartmentBadges names={tx.outgoingDepartmentNames} />
          </div>
        </div>
        <div className="transaction-hero-actions">
          {actionsSlot}
        </div>
      </div>

      <div className="transaction-metric-grid">
        <div className="transaction-metric-tile">
          <span className="transaction-metric-label">تاريخ الوارد</span>
          <span className="transaction-metric-value"><DateDisplay date={tx.incomingDate} /></span>
          <small className="text-muted metric-hint">بداية عمر المعاملة وأيام الإنجاز</small>
        </div>
        <div className="transaction-metric-tile">
          <span className="transaction-metric-label">تاريخ استحقاق المعاملة</span>
          <span className="transaction-metric-value">
            {tx.responseDueDate ? <DateDisplay date={tx.responseDueDate} /> : '—'}
          </span>
          <small className="text-muted metric-hint">آخر تاريخ متوقع لإغلاق جميع الإحالات</small>
        </div>
        <div className="transaction-metric-tile">
          <span className="transaction-metric-label">تاريخ إغلاق المعاملة</span>
          <span className="transaction-metric-value">
            {effectiveCompletionDate ? <DateDisplay date={effectiveCompletionDate} /> : '—'}
          </span>
          <small className="text-muted metric-hint">
            {completionDateHint}
          </small>
        </div>
        <div className="transaction-metric-tile">
          <span className="transaction-metric-label">
            {completionDaysLabel}
          </span>
          <span className="transaction-metric-value">
            {completionDaysValue}
          </span>
          <small className="text-muted metric-hint">
            {completionDaysHint}
          </small>
        </div>
        <div className={`transaction-metric-tile${tx.isResponseOverdue ? ' metric-tile-overdue' : ''}`}>
          <span className="transaction-metric-label">حالة التأخر</span>
          <span className={`transaction-metric-value${tx.isResponseOverdue ? ' text-danger' : ' text-success'}`}>
            {responseTimingLabel}
          </span>
          <small className="text-muted metric-hint">
            {tx.responseDueDate
              ? `${formatDaysRemaining(tx.daysRemainingForResponse)} حتى الاستحقاق`
              : 'لم يُحدَّد تاريخ استحقاق'}
          </small>
        </div>
        <div className="transaction-metric-tile">
          <span className="transaction-metric-label">منذ آخر تعقيب</span>
          <span className="transaction-metric-value">{formatDaysSince(tx.daysSinceLastFollowUp)}</span>
        </div>
        <div className="transaction-metric-tile">
          <span className="transaction-metric-label">احالةات مفتوحة</span>
          <span className="transaction-metric-value">{openAssignmentsCount}</span>
        </div>
        <div className="transaction-metric-tile">
          <span className="transaction-metric-label">المرفقات</span>
          <span className="transaction-metric-value">{attachmentsCount}</span>
        </div>
      </div>

      {children}
    </section>
  );
}
