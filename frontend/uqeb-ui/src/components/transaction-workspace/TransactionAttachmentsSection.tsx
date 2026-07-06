import type { Attachment } from '../../api/types';
import DateDisplay from '../DateDisplay';
import { Alert, LoadingInline } from '../ui';
import CardActionPanel from './CardActionPanel';
import AttachmentFormPanel from './AttachmentFormPanel';
import type { WorkspaceAction } from './types';

function isPreviewableAttachment(contentType?: string): boolean {
  if (!contentType) return false;
  return contentType.startsWith('image/') || contentType === 'application/pdf';
}

type TransactionAttachmentsSectionProps = Readonly<{
  transactionId: string;
  attachments: Attachment[];
  attachmentsLoading: boolean;
  attachmentsError: string;
  onRetryLoad: () => void;
  showMutationActions: boolean;
  activeAction: WorkspaceAction | null;
  onToggleAction: (action: WorkspaceAction) => void;
  onCloseAction: () => void;
  onDirtyChange: (dirty: boolean) => void;
  onAttachmentSuccess: () => void | Promise<void>;
  onDownload: (attachmentId: number, fileName: string) => void | Promise<void>;
  onPreview: (attachmentId: number, contentType?: string) => void | Promise<void>;
}>;

export default function TransactionAttachmentsSection({
  transactionId,
  attachments,
  attachmentsLoading,
  attachmentsError,
  onRetryLoad,
  showMutationActions,
  activeAction,
  onToggleAction,
  onCloseAction,
  onDirtyChange,
  onAttachmentSuccess,
  onDownload,
  onPreview,
}: TransactionAttachmentsSectionProps) {
  return (
    <section className="card transaction-section-card" aria-label="المرفقات">
      <div className="section-card-header">
        <div className="section-card-title">
          <span className="section-card-icon" aria-hidden>📎</span>
          <h3>المرفقات</h3>
          <span className="section-card-count">{attachments.length} مرفق</span>
        </div>
        {showMutationActions && (
          <button
            type="button"
            className={`btn btn-secondary btn-sm${activeAction === 'attachment' ? ' active' : ''}`}
            aria-pressed={activeAction === 'attachment'}
            onClick={() => onToggleAction('attachment')}
          >
            + إضافة مرفق
          </button>
        )}
      </div>

      {activeAction === 'attachment' && (
        <CardActionPanel
          title="إضافة مرفق"
          onClose={onCloseAction}
          testId="attachment-form-panel"
        >
          <AttachmentFormPanel
            transactionId={+transactionId}
            onDirtyChange={onDirtyChange}
            onCancel={onCloseAction}
            onSuccess={onAttachmentSuccess}
          />
        </CardActionPanel>
      )}

      {attachmentsLoading && <LoadingInline label="جاري تحميل المرفقات..." />}
      {attachmentsError && (
        <Alert variant="error">
          {attachmentsError}
          <button type="button" className="btn btn-sm btn-outline ms-2" onClick={onRetryLoad}>
            إعادة المحاولة
          </button>
        </Alert>
      )}
      {!attachmentsLoading && !attachmentsError && attachments.length === 0 && (
        <div className="section-empty-state">
          <p>لا توجد مرفقات لهذه المعاملة.</p>
          {showMutationActions && (
            <button type="button" className="btn btn-primary btn-sm" onClick={() => onToggleAction('attachment')}>
              إضافة أول مرفق
            </button>
          )}
        </div>
      )}
      {!attachmentsLoading && !attachmentsError && attachments.length > 0 && (
        <div className="attachment-list section-data-list">
          {attachments.map((a) => (
            <article key={a.id} className="attachment-row-card">
              <div className="attachment-row-main">
                <strong>{a.originalFileName}</strong>
                <span className="text-muted">{(a.fileSize / 1024).toFixed(1)} KB</span>
              </div>
              <div className="attachment-row-meta text-muted">
                {a.uploadedByName} • <DateDisplay date={a.uploadedAt} />
              </div>
              <div className="attachment-row-actions">
                <button type="button" className="btn btn-sm btn-outline" onClick={() => onDownload(a.id, a.originalFileName)}>
                  تحميل
                </button>
                {isPreviewableAttachment(a.contentType) && (
                  <button type="button" className="btn btn-sm btn-outline" onClick={() => onPreview(a.id, a.contentType)}>
                    معاينة
                  </button>
                )}
              </div>
            </article>
          ))}
        </div>
      )}
    </section>
  );
}
