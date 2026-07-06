import type { Assignment, FollowUp, TransactionDetail } from '../../api/types';
import DateDisplay from '../DateDisplay';
import { Alert, LoadingInline } from '../ui';
import { replyStatusLabels } from '../../utils/labels';
import CardActionPanel from './CardActionPanel';
import FollowUpFormPanel from './FollowUpFormPanel';
import ReplyFormPanel from './ReplyFormPanel';
import FollowUpLetterFormPanel from './FollowUpLetterFormPanel';
import type { WorkspaceAction, WorkspaceActionContext } from './types';
import { transactionsApi } from '../../api/services';

type TransactionFollowUpsSectionProps = Readonly<{
  transactionId: string;
  tx: TransactionDetail;
  assignments: Assignment[];
  followUps: FollowUp[];
  followUpsLoading: boolean;
  followUpsError: string;
  onRetryLoad: () => void;
  showMutationActions: boolean;
  canReply: boolean;
  activeAction: WorkspaceAction | null;
  replyFollowUpId?: number;
  onToggleAction: (action: WorkspaceAction, ctx?: WorkspaceActionContext) => void;
  onOpenAction: (action: WorkspaceAction, ctx?: WorkspaceActionContext) => void;
  onCloseAction: () => void;
  onDirtyChange: (dirty: boolean) => void;
  onFollowUpSuccess: () => void | Promise<void>;
  onReplyFollowUpSuccess: () => void | Promise<void>;
  onFollowUpLetterDownloaded: () => void;
}>;

export default function TransactionFollowUpsSection({
  transactionId,
  tx,
  assignments,
  followUps,
  followUpsLoading,
  followUpsError,
  onRetryLoad,
  showMutationActions,
  canReply,
  activeAction,
  replyFollowUpId,
  onToggleAction,
  onOpenAction,
  onCloseAction,
  onDirtyChange,
  onFollowUpSuccess,
  onReplyFollowUpSuccess,
  onFollowUpLetterDownloaded,
}: TransactionFollowUpsSectionProps) {
  return (
    <section className="card transaction-section-card" aria-label="التعقيبات">
      <div className="section-card-header">
        <div className="section-card-title">
          <span className="section-card-icon" aria-hidden>✉</span>
          <h3>التعقيبات</h3>
          <span className="section-card-count">{followUps.length} تعقيب</span>
        </div>
        {showMutationActions && (
          <div className="section-card-header-actions">
            <button
              type="button"
              className={`btn btn-secondary btn-sm${activeAction === 'followup' ? ' active' : ''}`}
              aria-pressed={activeAction === 'followup'}
              onClick={() => onToggleAction('followup')}
            >
              + إضافة تعقيب
            </button>
            <button
              type="button"
              className={`btn btn-secondary btn-sm${activeAction === 'follow-up-letter' ? ' active' : ''}`}
              aria-pressed={activeAction === 'follow-up-letter'}
              onClick={() => onToggleAction('follow-up-letter')}
            >
              خطاب تعقيب PDF
            </button>
          </div>
        )}
      </div>

      {activeAction === 'followup' && (
        <CardActionPanel
          title="إضافة تعقيب"
          onClose={onCloseAction}
          testId="followup-form-panel"
        >
          <FollowUpFormPanel
            transactionId={+transactionId}
            daysSinceLastFollowUp={tx.daysSinceLastFollowUp}
            onDirtyChange={onDirtyChange}
            onCancel={onCloseAction}
            onSuccess={onFollowUpSuccess}
          />
        </CardActionPanel>
      )}

      {activeAction === 'reply-followup' && replyFollowUpId && (
        <CardActionPanel
          title="تسجيل رد على التعقيب"
          onClose={onCloseAction}
          testId="reply-followup-form-panel"
        >
          <ReplyFormPanel
            title="تسجيل رد على التعقيب"
            onDirtyChange={onDirtyChange}
            onCancel={onCloseAction}
            onSubmit={(payload) => transactionsApi.replyFollowUp(+transactionId, replyFollowUpId, payload)}
            onSuccess={onReplyFollowUpSuccess}
          />
        </CardActionPanel>
      )}

      {activeAction === 'follow-up-letter' && (
        <CardActionPanel
          title="خطاب تعقيب PDF"
          onClose={onCloseAction}
          testId="follow-up-letter-form-panel"
        >
          <FollowUpLetterFormPanel
            transactionId={+transactionId}
            tx={tx}
            assignments={assignments}
            onDirtyChange={onDirtyChange}
            onCancel={onCloseAction}
            onDownloaded={onFollowUpLetterDownloaded}
          />
        </CardActionPanel>
      )}

      {followUpsLoading && <LoadingInline label="جاري تحميل التعقيبات..." />}
      {followUpsError && (
        <Alert variant="error">
          {followUpsError}
          <button type="button" className="btn btn-sm btn-outline ms-2" onClick={onRetryLoad}>
            إعادة المحاولة
          </button>
        </Alert>
      )}
      {!followUpsLoading && !followUpsError && followUps.length === 0 && (
        <div className="section-empty-state">
          <p>لا توجد تعقيبات مسجلة لهذه المعاملة.</p>
          {showMutationActions && (
            <button type="button" className="btn btn-primary btn-sm" onClick={() => onToggleAction('followup')}>
              إضافة أول تعقيب
            </button>
          )}
        </div>
      )}
      {!followUpsLoading && !followUpsError && followUps.length > 0 && (
        <div className="table-wrapper section-data-list">
          <table className="data-table data-table-compact">
            <thead><tr><th>الرقم</th><th>التاريخ</th><th>مرسل إلى</th><th>الرد</th><th>إجراء</th></tr></thead>
            <tbody>
              {followUps.map((f) => (
                <tr key={f.id}>
                  <td>{f.followUpNumber || '—'}</td>
                  <td><DateDisplay date={f.followUpDate} /></td>
                  <td>{f.departments?.length > 0 ? f.departments.map((d) => d.departmentName).join('، ') : f.sentTo || '—'}</td>
                  <td>
                    <span className={`badge ${f.replyStatus === 'Replied' ? 'badge-green' : 'badge-orange'}`}>
                      {replyStatusLabels[f.replyStatus] || f.replyStatus}
                    </span>
                  </td>
                  <td>
                    {f.requiresReply && f.replyStatus !== 'Replied' && canReply && (
                      <button
                        type="button"
                        className="btn btn-sm btn-outline"
                        onClick={() => onOpenAction('reply-followup', { replyFollowUpId: f.id })}
                      >
                        تسجيل رد
                      </button>
                    )}
                    {f.replySummary && <div className="text-muted reply-summary">{f.replySummary}</div>}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}
