import { Link } from 'react-router-dom';
import type { WorkspaceAction } from './types';

type TransactionActionBarProps = Readonly<{
  transactionId: string;
  canEdit: boolean;
  canClose: boolean;
  isDepartmentUser: boolean;
  canRegisterResponse: boolean;
  canShowClose: boolean;
  hasPendingDepts: boolean;
  activeAction: WorkspaceAction | null;
  onAction: (action: WorkspaceAction) => void;
  onCloseTransaction: () => void;
}>;

export default function TransactionActionBar({
  transactionId,
  canEdit,
  canClose,
  isDepartmentUser,
  canRegisterResponse,
  canShowClose,
  hasPendingDepts,
  activeAction,
  onAction,
  onCloseTransaction,
}: TransactionActionBarProps) {
  const showMutationActions = canEdit && !isDepartmentUser;

  return (
    <nav className="workspace-action-bar" aria-label="إجراءات المعاملة">
      {showMutationActions && (
        <>
          <button
            type="button"
            className={`btn btn-secondary btn-sm${activeAction === 'assignment' ? ' active' : ''}`}
            onClick={() => onAction('assignment')}
          >
            إضافة تحويل
          </button>
          <button
            type="button"
            className={`btn btn-secondary btn-sm${activeAction === 'followup' ? ' active' : ''}`}
            onClick={() => onAction('followup')}
          >
            إضافة تعقيب
          </button>
          <button
            type="button"
            className={`btn btn-secondary btn-sm${activeAction === 'attachment' ? ' active' : ''}`}
            onClick={() => onAction('attachment')}
          >
            إضافة مرفق
          </button>
          <Link to={`/transactions/${transactionId}/edit`} className="btn btn-outline btn-sm">
            تعديل
          </Link>
          <button
            type="button"
            className={`btn btn-secondary btn-sm${activeAction === 'follow-up-letter' ? ' active' : ''}`}
            onClick={() => onAction('follow-up-letter')}
          >
            خطاب تعقيب PDF
          </button>
        </>
      )}
      {canRegisterResponse && (
        <button
          type="button"
          className={`btn btn-primary btn-sm${activeAction === 'complete-response' ? ' active' : ''}`}
          disabled={hasPendingDepts}
          title={hasPendingDepts ? 'لا يمكن تسجيل الإفادة قبل اكتمال رد جميع الإدارات.' : undefined}
          onClick={() => onAction('complete-response')}
        >
          تسجيل الإفادة
        </button>
      )}
      {canShowClose && (
        <button type="button" className="btn btn-danger btn-sm" onClick={onCloseTransaction}>
          إغلاق المعاملة
        </button>
      )}
      {!showMutationActions && !canRegisterResponse && !canShowClose && canClose && (
        <span className="text-muted workspace-action-hint">عرض فقط — لا تتوفر إجراءات تعديل</span>
      )}
    </nav>
  );
}
