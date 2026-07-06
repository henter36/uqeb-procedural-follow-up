import type { TransactionDetail } from '../../api/types';
import DateDisplay from '../DateDisplay';

type TransactionActionStatusCardProps = Readonly<{
  tx: TransactionDetail;
  needsResponse: boolean;
  isTerminal: boolean;
  isDepartmentUser: boolean;
  canRegisterResponse: boolean;
  departmentResponseActionStatusLabel?: string;
}>;

function truncate(text: string, max = 140): string {
  const trimmed = text.trim();
  if (trimmed.length <= max) return trimmed;
  return `${trimmed.slice(0, max).trimEnd()}…`;
}

function getRequiredActionText({
  tx,
  needsResponse,
  isTerminal,
  isDepartmentUser,
  canRegisterResponse,
  departmentResponseActionStatusLabel,
}: TransactionActionStatusCardProps): string {
  if (isTerminal) return 'تم إغلاق المعاملة. لا يوجد إجراء مطلوب حاليًا.';
  if (!needsResponse) return 'لا يوجد إجراء مطلوب حاليًا لهذه المعاملة.';
  if (tx.responseCompleted) return 'تمت الإفادة. بانتظار إغلاق المعاملة.';

  if (isDepartmentUser) {
    if (canRegisterResponse) return 'المطلوب من إدارتكم: تسجيل الإفادة.';
    if (departmentResponseActionStatusLabel) return `إفادة إدارتكم ${departmentResponseActionStatusLabel} — بانتظار المراجعة.`;
    return 'لا يوجد إجراء مطلوب من إدارتكم حاليًا.';
  }

  if (tx.pendingDepartmentNames.length > 0) return 'بانتظار رد الإدارات المكلفة قبل تسجيل الإفادة.';
  return 'المطلوب: تسجيل الإفادة.';
}

export default function TransactionActionStatusCard({
  tx,
  needsResponse,
  isTerminal,
  isDepartmentUser,
  canRegisterResponse,
  departmentResponseActionStatusLabel,
}: TransactionActionStatusCardProps) {
  const requiredActionText = getRequiredActionText({
    tx, needsResponse, isTerminal, isDepartmentUser, canRegisterResponse, departmentResponseActionStatusLabel,
  });
  const pendingDepartmentNames = tx.pendingDepartmentNames ?? [];
  const hasLatestResponse = tx.responseCompleted && Boolean(tx.responseSummary);

  return (
    <section className="card transaction-section-card transaction-action-status-card" aria-label="حالة الإجراء الحالية">
      <div className="section-card-header">
        <div className="section-card-title">
          <span className="section-card-icon" aria-hidden>◉</span>
          <h3>حالة الإجراء الحالية</h3>
        </div>
      </div>

      <div className="action-status-grid">
        <div className="action-status-item">
          <span className="action-status-label">الإجراء المطلوب</span>
          <p className="action-status-value">{requiredActionText}</p>
        </div>

        <div className="action-status-item">
          <span className="action-status-label">الإدارات بانتظار الرد</span>
          {pendingDepartmentNames.length > 0 ? (
            <p className="action-status-value">
              {pendingDepartmentNames.length} إدارة: {pendingDepartmentNames.join('، ')}
            </p>
          ) : (
            <p className="action-status-value text-muted">لا توجد إدارات بانتظار الرد.</p>
          )}
        </div>

        <div className="action-status-item">
          <span className="action-status-label">آخر إفادة</span>
          {hasLatestResponse ? (
            <p className="action-status-value">
              {tx.responseCompletedDate && <><DateDisplay date={tx.responseCompletedDate} />{' — '}</>}
              {truncate(tx.responseSummary!)}
              {' '}
              <a href="#transaction-responses-section">عرض التفاصيل</a>
            </p>
          ) : (
            <p className="action-status-value text-muted">
              لم تُسجَّل أي إفادة بعد.
              {' '}
              <a href="#transaction-responses-section">الانتقال إلى قسم الإفادة</a>
            </p>
          )}
        </div>
      </div>
    </section>
  );
}
