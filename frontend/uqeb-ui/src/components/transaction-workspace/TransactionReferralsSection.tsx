import type { Assignment, Department } from '../../api/types';
import DateDisplay from '../DateDisplay';
import { Alert, LoadingInline } from '../ui';
import { replyStatusLabels } from '../../utils/labels';
import CardActionPanel from './CardActionPanel';
import AssignmentFormPanel from './AssignmentFormPanel';
import ReplyFormPanel from './ReplyFormPanel';
import AdminEditAssignmentFormPanel from './AdminEditAssignmentFormPanel';
import AdminEditResponseFormPanel from './AdminEditResponseFormPanel';
import type { WorkspaceAction, WorkspaceActionContext } from './types';
import { transactionsApi } from '../../api/services';

function assignmentReplyBadgeClass(replyStatus: string, isOverdue: boolean): string {
  if (replyStatus === 'Replied') return 'badge-green';
  if (isOverdue) return 'badge-red';
  return 'badge-orange';
}

function getAssignmentReplyStatusLabel(replyStatus: string): string {
  if (replyStatus === 'Replied') return 'تمت الإفادة';
  return replyStatusLabels[replyStatus] || replyStatus;
}

function renderDepartmentCompletionDays(completionDays?: number | null) {
  if (completionDays != null) {
    return <>{completionDays} <small className="text-muted">يوم</small></>;
  }
  return <span className="text-muted">لم تُنجز الإدارة</span>;
}

type TransactionReferralsSectionProps = Readonly<{
  transactionId: string;
  assignments: Assignment[];
  assignmentsLoading: boolean;
  assignmentsError: string;
  onRetryLoad: () => void;
  ageText: string;
  departments: Department[];
  fallbackLetterNumber?: string;
  showMutationActions: boolean;
  canReply: boolean;
  isAdmin: boolean;
  activeAction: WorkspaceAction | null;
  replyAssignmentId?: number;
  adminEditAssignmentId?: number;
  adminEditResponseId?: number;
  onToggleAction: (action: WorkspaceAction, ctx?: WorkspaceActionContext) => void;
  onOpenAction: (action: WorkspaceAction, ctx?: WorkspaceActionContext) => void;
  onCloseAction: () => void;
  onDirtyChange: (dirty: boolean) => void;
  onAssignmentSuccess: () => void | Promise<void>;
  onReplyAssignmentSuccess: () => void | Promise<void>;
  onAdminEditAssignmentSuccess: (updated: Assignment) => void;
  onAdminEditResponseSuccess: () => void;
}>;

export default function TransactionReferralsSection({
  transactionId,
  assignments,
  assignmentsLoading,
  assignmentsError,
  onRetryLoad,
  ageText,
  departments,
  fallbackLetterNumber,
  showMutationActions,
  canReply,
  isAdmin,
  activeAction,
  replyAssignmentId,
  adminEditAssignmentId,
  adminEditResponseId,
  onToggleAction,
  onOpenAction,
  onCloseAction,
  onDirtyChange,
  onAssignmentSuccess,
  onReplyAssignmentSuccess,
  onAdminEditAssignmentSuccess,
  onAdminEditResponseSuccess,
}: TransactionReferralsSectionProps) {
  const existingDepartmentIds = assignments.map((a) => a.departmentId);

  return (
    <section className="card transaction-section-card" aria-label="الاحالات">
      <div className="section-card-header">
        <div className="section-card-title">
          <span className="section-card-icon" aria-hidden>↪</span>
          <h3>الاحالات</h3>
          <span className="section-card-count">{assignments.length} احالة</span>
          <span className="section-card-meta">{ageText}</span>
        </div>
        {showMutationActions && (
          <button
            type="button"
            className={`btn btn-secondary btn-sm${activeAction === 'assignment' ? ' active' : ''}`}
            aria-pressed={activeAction === 'assignment'}
            onClick={() => onToggleAction('assignment')}
          >
            + إضافة احالة
          </button>
        )}
      </div>

      {activeAction === 'assignment' && (
        <CardActionPanel
          title="إضافة احالة"
          onClose={onCloseAction}
          testId="assignment-form-panel"
        >
          <AssignmentFormPanel
            transactionId={+transactionId}
            departments={departments}
            existingDepartmentIds={existingDepartmentIds}
            onDirtyChange={onDirtyChange}
            onCancel={onCloseAction}
            onSuccess={onAssignmentSuccess}
          />
        </CardActionPanel>
      )}

      {activeAction === 'reply-assignment' && replyAssignmentId && (
        <CardActionPanel
          title="تسجيل إفادة الإدارة"
          onClose={onCloseAction}
          testId="reply-assignment-form-panel"
        >
          <ReplyFormPanel
            title="تسجيل إفادة الإدارة"
            dateLabel="تاريخ إنجاز الإدارة"
            dateHint="يمثل تاريخ الإفادة/إنجاز رد الإدارة، ويستخدم في احتساب أيام إنجاز الإدارة."
            dateRequiredMessage="تاريخ إنجاز الإدارة مطلوب."
            summaryLabel="ملخص الإفادة *"
            submitLabel="حفظ الإفادة"
            onDirtyChange={onDirtyChange}
            onCancel={onCloseAction}
            onSubmit={(payload) => transactionsApi.replyAssignment(+transactionId, replyAssignmentId, payload)}
            onSuccess={onReplyAssignmentSuccess}
          />
        </CardActionPanel>
      )}

      {activeAction === 'admin-edit-assignment' && adminEditAssignmentId && (
        <CardActionPanel
          title="تعديل بيانات الاحالة"
          onClose={onCloseAction}
          testId="admin-edit-assignment-form-panel"
        >
          <AdminEditAssignmentFormPanel
            key={adminEditAssignmentId}
            transactionId={+transactionId}
            assignmentId={adminEditAssignmentId}
            initialAssignment={assignments.find((a) => a.id === adminEditAssignmentId)}
            fallbackLetterNumber={fallbackLetterNumber}
            onDirtyChange={onDirtyChange}
            onCancel={onCloseAction}
            onSuccess={onAdminEditAssignmentSuccess}
          />
        </CardActionPanel>
      )}

      {activeAction === 'admin-edit-response' && adminEditResponseId && (
        <CardActionPanel
          title="تعديل بيانات الإفادة"
          onClose={onCloseAction}
          testId="admin-edit-response-form-panel"
        >
          <AdminEditResponseFormPanel
            responseId={adminEditResponseId}
            onDirtyChange={onDirtyChange}
            onCancel={onCloseAction}
            onSuccess={onAdminEditResponseSuccess}
          />
        </CardActionPanel>
      )}

      {assignmentsLoading && <LoadingInline label="جاري تحميل الاحالةات..." />}
      {assignmentsError && (
        <Alert variant="error">
          {assignmentsError}
          <button type="button" className="btn btn-sm btn-outline ms-2" onClick={onRetryLoad}>
            إعادة المحاولة
          </button>
        </Alert>
      )}
      {!assignmentsLoading && !assignmentsError && assignments.length === 0 && (
        <div className="section-empty-state">
          <p>لا توجد احالةات مسجلة لهذه المعاملة.</p>
          {showMutationActions && (
            <button type="button" className="btn btn-primary btn-sm" onClick={() => onToggleAction('assignment')}>
              إضافة أول احالة
            </button>
          )}
        </div>
      )}
      {!assignmentsLoading && !assignmentsError && assignments.length > 0 && (
        <div className="table-wrapper section-data-list">
          <table className="data-table data-table-compact">
            <thead>
              <tr>
                <th>الإدارة</th>
                <th>رقم الخطاب</th>
                <th>تاريخ الإحالة</th>
                <th>تاريخ استحقاق الإدارة</th>
                <th>تاريخ إنجاز الإدارة</th>
                <th>أيام إنجاز الإدارة</th>
                <th>الحالة</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {assignments.map((a) => {
                const replyStatusLabel = getAssignmentReplyStatusLabel(a.replyStatus);
                const canOpenAdminAssignmentEdit = isAdmin && a.canAdminEdit === true;
                const canOpenAdminResponseEdit = isAdmin && a.replyStatus === 'Replied' && Boolean(a.departmentResponseId);

                return (
                  <tr key={a.id} className={a.isOverdue ? 'row-overdue' : ''}>
                    <td>
                      <div>
                        {canOpenAdminAssignmentEdit ? (
                          <button
                            type="button"
                            className="link-button assignment-department-link"
                            aria-label={`تعديل إحالة إدارة ${a.departmentName}`}
                            onClick={() => onOpenAction('admin-edit-assignment', { adminEditAssignmentId: a.id })}
                          >
                            {a.departmentName}
                          </button>
                        ) : (
                          <span>{a.departmentName}</span>
                        )}
                      </div>
                      {a.requiredAction && <div className="text-muted">{a.requiredAction}</div>}
                    </td>
                    <td>{a.letterNumber || fallbackLetterNumber || '—'}</td>
                    <td><DateDisplay date={a.assignedDate} /></td>
                    <td>{a.dueDate ? <DateDisplay date={a.dueDate} /> : '—'}</td>
                    <td>{a.responseDate ? <DateDisplay date={a.responseDate} /> : '—'}</td>
                    <td>
                      {renderDepartmentCompletionDays(a.departmentCompletionDays)}
                    </td>
                    <td>
                      {canOpenAdminResponseEdit ? (
                        <button
                          type="button"
                          className={`badge assignment-response-status-link ${assignmentReplyBadgeClass(a.replyStatus, a.isOverdue)}`}
                          aria-label={`تعديل إفادة إدارة ${a.departmentName}`}
                          onClick={() => onOpenAction('admin-edit-response', { adminEditResponseId: a.departmentResponseId! })}
                        >
                          {replyStatusLabel}
                        </button>
                      ) : (
                        <span className={`badge ${assignmentReplyBadgeClass(a.replyStatus, a.isOverdue)}`}>
                          {replyStatusLabel}
                        </span>
                      )}
                      {a.isOverdue && a.replyStatus !== 'Replied' && (
                        <span className="badge badge-red ms-1">متأخرة</span>
                      )}
                    </td>
                    <td className="assignment-actions-cell">
                      {a.requiresReply && a.replyStatus !== 'Replied' && a.status !== 'Cancelled' && canReply && (
                        <button
                          type="button"
                          className="btn btn-sm btn-outline"
                          onClick={() => onOpenAction('reply-assignment', { replyAssignmentId: a.id })}
                        >
                          تسجيل رد
                        </button>
                      )}
                      {a.replySummary && <div className="text-muted reply-summary">{a.replySummary}</div>}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}
