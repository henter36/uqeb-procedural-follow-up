import { Link } from 'react-router-dom';
import type { WorkspaceAction } from './types';

type TransactionActionBarProps = Readonly<{
  transactionId: string;
  canEdit: boolean;
  canClose: boolean;
  isDepartmentUser: boolean;
  canRegisterResponse: boolean;
  responseActionLabel?: string;
  responseStatusLabel?: string;
  canShowClose: boolean;
  hasPendingDepts: boolean;
  activeAction: WorkspaceAction | null;
  onAction: (action: WorkspaceAction) => void;
  onCloseTransaction: () => void | Promise<void>;
  closeInProgress?: boolean;
  showEnableRecurring?: boolean;
  showAdminEditDates?: boolean;
}>;

export default function TransactionActionBar({
  transactionId,
  canEdit,
  canClose,
  isDepartmentUser,
  canRegisterResponse,
  responseActionLabel = 'تسجيل الإفادة',
  responseStatusLabel,
  canShowClose,
  hasPendingDepts,
  activeAction,
  onAction,
  onCloseTransaction,
  closeInProgress = false,
  showEnableRecurring = false,
  showAdminEditDates = false,
}: TransactionActionBarProps) {
  const showMutationActions = canEdit && !isDepartmentUser;

  return (
    <nav className="workspace-action-bar" aria-label="إجراءات المعاملة">
      {showMutationActions && (
        <Link to={`/transactions/${transactionId}/edit`} className="btn btn-outline btn-sm">
          تعديل
        </Link>
      )}
      {showEnableRecurring && (
        <button
          type="button"
          className={`btn btn-outline btn-sm${activeAction === 'enable-recurring' ? ' active' : ''}`}
          aria-pressed={activeAction === 'enable-recurring'}
          onClick={() => onAction('enable-recurring')}
        >
          تفعيل متابعة دورية لهذه المعاملة
        </button>
      )}
      {showAdminEditDates && (
        <button
          type="button"
          className={`btn btn-outline btn-sm${activeAction === 'admin-edit-dates' ? ' active' : ''}`}
          aria-pressed={activeAction === 'admin-edit-dates'}
          onClick={() => onAction('admin-edit-dates')}
        >
          تصحيح التواريخ إداريًا
        </button>
      )}
      {canRegisterResponse && (
        <button
          type="button"
          className={`btn btn-primary btn-sm${activeAction === 'complete-response' ? ' active' : ''}`}
          disabled={!isDepartmentUser && hasPendingDepts}
          title={!isDepartmentUser && hasPendingDepts ? 'لا يمكن تسجيل الإفادة قبل اكتمال رد جميع الإدارات.' : undefined}
          onClick={() => onAction('complete-response')}
        >
          {responseActionLabel}
        </button>
      )}
      {!canRegisterResponse && responseStatusLabel && (
        <span className="badge badge-blue workspace-response-status">{responseStatusLabel}</span>
      )}
      {canShowClose && (
        <button
          type="button"
          className="btn btn-danger btn-sm"
          onClick={onCloseTransaction}
          disabled={closeInProgress}
        >
          إغلاق المعاملة
        </button>
      )}
      {!showMutationActions && !canRegisterResponse && !canShowClose && canClose && (
        <span className="text-muted workspace-action-hint">عرض فقط — لا تتوفر إجراءات تعديل</span>
      )}
    </nav>
  );
}
