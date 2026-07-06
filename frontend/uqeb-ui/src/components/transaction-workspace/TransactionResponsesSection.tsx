import type { Ref } from 'react';
import type {
  DepartmentResponseDto, DepartmentTransactionResponseItemDto, TransactionDetail,
} from '../../api/types';
import DateDisplay from '../DateDisplay';
import { responseTypeLabels } from '../../utils/labels';
import CardActionPanel from './CardActionPanel';
import DepartmentResponseInlinePanel from './DepartmentResponseInlinePanel';
import CompleteResponseFormPanel, { type CompleteResponseSuccessResult } from './CompleteResponseFormPanel';
import type { WorkspaceAction } from './types';

type TransactionResponsesSectionProps = Readonly<{
  transactionId: string;
  tx: TransactionDetail;
  isDepartmentUser: boolean;
  activeAction: WorkspaceAction | null;
  responseActionLabel: string;
  departmentResponseItem: DepartmentTransactionResponseItemDto | null;
  panelRef: Ref<HTMLElement>;
  onDirtyChange: (dirty: boolean) => void;
  onCancel: () => void;
  onMessage: (message: string) => void;
  onDepartmentResponseChanged: (response: DepartmentResponseDto) => void | Promise<void>;
  onCompleteResponseSuccess: (result?: CompleteResponseSuccessResult) => void | Promise<void>;
}>;

export default function TransactionResponsesSection({
  transactionId,
  tx,
  isDepartmentUser,
  activeAction,
  responseActionLabel,
  departmentResponseItem,
  panelRef,
  onDirtyChange,
  onCancel,
  onMessage,
  onDepartmentResponseChanged,
  onCompleteResponseSuccess,
}: TransactionResponsesSectionProps) {
  const requiresOutgoing = tx.responseType === 'External' || tx.responseType === 'Both';
  const isPanelOpen = activeAction === 'complete-response';

  return (
    <section id="transaction-responses-section" className="card transaction-section-card" aria-label="الإفادة">
      <div className="section-card-header">
        <div className="section-card-title">
          <span className="section-card-icon" aria-hidden>🗨</span>
          <h3>الإفادة</h3>
          {tx.responseCompleted && <span className="badge badge-green">تمت الإفادة</span>}
        </div>
      </div>

      {isPanelOpen && (
        <CardActionPanel
          title={responseActionLabel}
          onClose={onCancel}
          testId="complete-response-form-panel"
          panelRef={panelRef}
          prominent
        >
          {isDepartmentUser ? (
            <DepartmentResponseInlinePanel
              transactionId={+transactionId}
              initialItem={departmentResponseItem}
              onDirtyChange={onDirtyChange}
              onCancel={onCancel}
              onMessage={onMessage}
              onChanged={onDepartmentResponseChanged}
            />
          ) : (
            <CompleteResponseFormPanel
              transactionId={+transactionId}
              responseType={tx.responseType}
              onDirtyChange={onDirtyChange}
              onCancel={onCancel}
              onSuccess={onCompleteResponseSuccess}
            />
          )}
        </CardActionPanel>
      )}

      {!isPanelOpen && !tx.responseCompleted && (
        <div className="section-empty-state">
          <p>لم تُسجَّل أي إفادة لهذه المعاملة بعد.</p>
        </div>
      )}

      {!isPanelOpen && tx.responseCompleted && (
        <div className="response-summary-card">
          <dl className="detail-grid">
            <div>
              <strong>تاريخ الإفادة:</strong>{' '}
              {tx.responseCompletedDate ? <DateDisplay date={tx.responseCompletedDate} /> : '—'}
            </div>
            <div><strong>نوع الإفادة:</strong> {responseTypeLabels[tx.responseType] || tx.responseType}</div>
            {requiresOutgoing && tx.outgoingNumber && <div><strong>رقم الصادر:</strong> {tx.outgoingNumber}</div>}
            {requiresOutgoing && tx.outgoingDate && (
              <div><strong>تاريخ الصادر:</strong> <DateDisplay date={tx.outgoingDate} /></div>
            )}
            {tx.responseSummary && (
              <div className="full-width"><strong>ملخص الإفادة:</strong> {tx.responseSummary}</div>
            )}
          </dl>
        </div>
      )}
    </section>
  );
}
