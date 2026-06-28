import { useCallback, useRef, useState } from 'react';
import { Link } from 'react-router-dom';
import { followUpPrintApi, transactionsApi } from '../api/services';
import type { FollowUp, FollowUpLetterPrintRecord } from '../api/types';
import { getApiErrorMessage } from '../utils/apiHelpers';
import { createIdempotencyKey } from '../utils/createIdempotencyKey';
import { sanitizeFullDocumentHtml } from '../utils/sanitizePrintHtml';
import DateDisplay from '../components/DateDisplay';
import FollowUpFormPanel from '../components/transaction-workspace/FollowUpFormPanel';
import {
  Alert, EmptyState, LoadingInline, PageHeader, Pagination,
} from '../components/ui';
import { useAuth } from '../context/useAuth';
import { useDeferredEffect } from '../hooks/useDeferredEffect';
import { usePendingPrintSummary } from '../hooks/usePendingPrintSummary';

type RecordHandlers = {
  onConfirm: (record: FollowUpLetterPrintRecord) => void;
  onCancel: (record: FollowUpLetterPrintRecord) => void;
  onReprint: (record: FollowUpLetterPrintRecord) => void;
  onRegisterFollowUp: (record: FollowUpLetterPrintRecord) => void;
  onLinkFollowUp: (record: FollowUpLetterPrintRecord) => void;
  onViewLetter: (record: FollowUpLetterPrintRecord) => void;
  actingId: number | null;
};

function getFollowUpReference(followUp: FollowUp): string {
  const visibleId = followUp.followUpNumber?.trim();
  return visibleId ? `${visibleId} · #${followUp.id}` : `#${followUp.id}`;
}

function getFollowUpTarget(followUp: FollowUp): string {
  const target = [
    followUp.sentTo?.trim(),
    followUp.recipients?.map((recipient) => recipient.partyName).filter(Boolean).join('، '),
    followUp.departments?.map((department) => department.departmentName).filter(Boolean).join('، '),
  ].find((part) => Boolean(part && part.length > 0));

  return target ?? '—';
}

function getFollowUpSnippet(followUp: FollowUp): string {
  return followUp.notes?.trim()
    || followUp.replySummary?.trim()
    || 'لا يوجد مختصر';
}

function PendingRecordRow({ record, handlers }: Readonly<{ record: FollowUpLetterPrintRecord; handlers: RecordHandlers }>) {
  const busy = handlers.actingId === record.id;
  const confirmed = Boolean(record.printConfirmedAt);

  return (
    <tr>
      <td>{record.incomingNumber}</td>
      <td>{record.subject}</td>
      <td>{record.targetEntityNameSnapshot ?? '—'}</td>
      <td>{record.followUpSequence}</td>
      <td><DateDisplay date={record.printRequestedAt} /></td>
      <td>
        {confirmed ? (
          <span className="badge badge-green">الطباعة مؤكدة</span>
        ) : (
          <span className="badge badge-yellow">الطباعة غير مؤكدة</span>
        )}
        <div className="text-muted">التعقيب غير مسجل</div>
        {record.printConfirmedAt && <DateDisplay date={record.printConfirmedAt} />}
      </td>
      <td className="btn-group">
        <Link to={`/transactions/${record.transactionId}`} className="btn btn-sm btn-outline">المعاملة</Link>
        <button type="button" className="btn btn-sm btn-secondary" disabled={busy} onClick={() => handlers.onViewLetter(record)}>
          عرض الخطاب
        </button>
        <button type="button" className="btn btn-sm btn-primary" disabled={busy || confirmed} onClick={() => handlers.onConfirm(record)}>
          {confirmed ? 'تم التأكيد' : 'تأكيد'}
        </button>
        <button type="button" className="btn btn-sm btn-primary" disabled={busy} onClick={() => handlers.onRegisterFollowUp(record)}>
          تسجيل التعقيب
        </button>
        <button type="button" className="btn btn-sm btn-outline" disabled={busy} onClick={() => handlers.onLinkFollowUp(record)}>
          ربط تعقيب موجود
        </button>
        <button type="button" className="btn btn-sm btn-outline" disabled={busy} onClick={() => handlers.onReprint(record)}>
          إعادة طباعة
        </button>
        <button type="button" className="btn btn-sm btn-outline" disabled={busy} onClick={() => handlers.onCancel(record)}>
          إلغاء
        </button>
      </td>
    </tr>
  );
}

export default function FollowUpPrintPendingPage() {
  const { canClose } = useAuth();
  const [records, setRecords] = useState<FollowUpLetterPrintRecord[]>([]);
  const [summaryTotal, setSummaryTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [loading, setLoading] = useState(true);
  const [actingId, setActingId] = useState<number | null>(null);
  const [previewHtml, setPreviewHtml] = useState('');
  const [previewWarning, setPreviewWarning] = useState('');
  const [error, setError] = useState('');
  const [message, setMessage] = useState('');
  const [linkDialogRecord, setLinkDialogRecord] = useState<FollowUpLetterPrintRecord | null>(null);
  const [linkFollowUps, setLinkFollowUps] = useState<FollowUp[]>([]);
  const [linkLoading, setLinkLoading] = useState(false);
  const [linkError, setLinkError] = useState('');
  const [linkingFollowUpId, setLinkingFollowUpId] = useState<number | null>(null);
  const [registerDialogRecord, setRegisterDialogRecord] = useState<FollowUpLetterPrintRecord | null>(null);
  const [cancelDialogRecord, setCancelDialogRecord] = useState<FollowUpLetterPrintRecord | null>(null);
  const [cancelReason, setCancelReason] = useState('');
  const [cancelSubmitting, setCancelSubmitting] = useState(false);
  const [cancelError, setCancelError] = useState('');
  const linkRequestSeq = useRef(0);
  const { refresh: refreshPendingSummary } = usePendingPrintSummary();

  const loadRecords = useCallback(async (active: () => boolean) => {
    await Promise.resolve();
    if (active()) {
      setLoading(true);
      setError('');
    }
    try {
      const [summaryRes, recordsRes] = await Promise.all([
        followUpPrintApi.getPendingSummary(),
        followUpPrintApi.getPending({ page, pageSize }),
      ]);
      if (!active()) return;
      setSummaryTotal(summaryRes.data.total);
      setRecords(recordsRes.data);
    } catch (err: unknown) {
      if (!active()) return;
      setError(getApiErrorMessage(err));
    } finally {
      if (active()) setLoading(false);
    }
  }, [page, pageSize]);

  useDeferredEffect(loadRecords, [loadRecords]);

  const refreshRecords = useCallback(async () => {
    await loadRecords(() => true);
  }, [loadRecords]);

  const closeRegisterDialog = useCallback(() => {
    setRegisterDialogRecord(null);
  }, []);

  const closeLinkDialog = useCallback(() => {
    linkRequestSeq.current += 1;
    setLinkDialogRecord(null);
    setLinkFollowUps([]);
    setLinkLoading(false);
    setLinkError('');
    setLinkingFollowUpId(null);
  }, []);

  const closeCancelDialog = useCallback(() => {
    setCancelDialogRecord(null);
    setCancelReason('');
    setCancelSubmitting(false);
    setCancelError('');
  }, []);

  const handleConfirm = async (record: FollowUpLetterPrintRecord) => {
    setActingId(record.id);
    setError('');
    try {
      const res = await followUpPrintApi.confirmRecord(record.id);
      setRecords((prev) => prev.map((item) => (item.id === record.id ? res.data : item)));
      setMessage(`تم تأكيد طباعة ${record.incomingNumber}.`);
      await refreshRecords();
      await refreshPendingSummary();
    } catch (err: unknown) {
      setError(getApiErrorMessage(err));
    } finally {
      setActingId(null);
    }
  };

  const handleViewLetter = async (record: FollowUpLetterPrintRecord) => {
    setActingId(record.id);
    setError('');
    setPreviewWarning('');
    try {
      const res = await followUpPrintApi.getRecordPrintView(record.id);
      setPreviewHtml(sanitizeFullDocumentHtml(res.data.html));
      setPreviewWarning(res.data.warning ?? '');
    } catch (err: unknown) {
      setError(getApiErrorMessage(err));
    } finally {
      setActingId(null);
    }
  };

  const handleOpenCancelDialog = (record: FollowUpLetterPrintRecord) => {
    setError('');
    setMessage('');
    setCancelDialogRecord(record);
    setCancelReason('');
    setCancelError('');
    setCancelSubmitting(false);
  };

  const handleSubmitCancel = async () => {
    if (!cancelDialogRecord) return;
    const trimmedReason = cancelReason.trim();
    if (!trimmedReason) {
      setCancelError('سبب الإلغاء مطلوب.');
      return;
    }

    setCancelSubmitting(true);
    setCancelError('');
    setError('');
    try {
      await followUpPrintApi.cancelRecord(cancelDialogRecord.id, trimmedReason);
      setMessage(`تم إلغاء سجل ${cancelDialogRecord.incomingNumber}.`);
      closeCancelDialog();
      await refreshRecords();
      await refreshPendingSummary();
    } catch (err: unknown) {
      setCancelError(getApiErrorMessage(err));
    } finally {
      setCancelSubmitting(false);
    }
  };

  const handleOpenRegisterDialog = (record: FollowUpLetterPrintRecord) => {
    setError('');
    setMessage('');
    setRegisterDialogRecord(record);
  };

  const handleFollowUpRegistered = async (followUp: FollowUp) => {
    if (!registerDialogRecord) return;
    const record = registerDialogRecord;
    closeRegisterDialog();
    setActingId(record.id);
    try {
      await followUpPrintApi.linkFollowUp(record.id, followUp.id);
      setMessage('تم تسجيل التعقيب وربطه بسجل الخطاب.');
      await refreshRecords();
      await refreshPendingSummary();
    } catch (err: unknown) {
      setError(`تم إنشاء التعقيب لكن فشل ربطه بسجل الخطاب: ${getApiErrorMessage(err)}`);
      await refreshRecords();
    } finally {
      setActingId(null);
    }
  };

  const handleOpenLinkDialog = async (record: FollowUpLetterPrintRecord) => {
    const requestSeq = linkRequestSeq.current + 1;
    linkRequestSeq.current = requestSeq;

    const isCurrentRequest = () => linkRequestSeq.current === requestSeq;

    setActingId(record.id);
    setError('');
    setMessage('');
    setLinkDialogRecord(record);
    setLinkFollowUps([]);
    setLinkError('');
    setLinkLoading(true);

    try {
      const res = await transactionsApi.getFollowUps(record.transactionId);
      if (!isCurrentRequest()) return;

      setLinkFollowUps(res.data);
    } catch (err: unknown) {
      if (!isCurrentRequest()) return;

      setLinkError(getApiErrorMessage(err));
    } finally {
      if (isCurrentRequest()) {
        setLinkLoading(false);
        setActingId(null);
      }
    }
  };

  const handleReprint = async (record: FollowUpLetterPrintRecord) => {
    setActingId(record.id);
    setError('');
    setMessage('');
    try {
      await followUpPrintApi.reprintRecord(record.id, createIdempotencyKey());
      setMessage(`تم إنشاء إعادة طباعة لـ ${record.incomingNumber}.`);
      await refreshRecords();
      await refreshPendingSummary();
    } catch (err: unknown) {
      setError(getApiErrorMessage(err));
    } finally {
      setActingId(null);
    }
  };

  const handleSelectFollowUp = async (followUp: FollowUp) => {
    if (!linkDialogRecord) return;

    setLinkingFollowUpId(followUp.id);
    setLinkError('');
    setMessage('');
    try {
      await followUpPrintApi.linkFollowUp(linkDialogRecord.id, followUp.id);
      setMessage(`تم ربط ${linkDialogRecord.incomingNumber} بالتعقيب ${getFollowUpReference(followUp)}.`);
      closeLinkDialog();
      await refreshRecords();
      await refreshPendingSummary();
    } catch (err: unknown) {
      setLinkError(getApiErrorMessage(err));
    } finally {
      setLinkingFollowUpId(null);
    }
  };

  const handlers: RecordHandlers = {
    onConfirm: (r) => { handleConfirm(r).catch(() => undefined); },
    onCancel: (r) => { handleOpenCancelDialog(r); },
    onReprint: (r) => { handleReprint(r).catch(() => undefined); },
    onRegisterFollowUp: (r) => { handleOpenRegisterDialog(r); },
    onLinkFollowUp: (r) => { handleOpenLinkDialog(r).catch(() => undefined); },
    onViewLetter: (r) => { handleViewLetter(r).catch(() => undefined); },
    actingId,
  };

  const renderRecordsContent = () => {
    if (loading) {
      return <LoadingInline label="جاري التحميل..." />;
    }
    if (records.length === 0) {
      return <EmptyState title="لا توجد سجلات معلقة" description="جميع الخطابات المطبوعة مسجلة." />;
    }
    return (
      <>
        <div className="table-wrapper table-wrapper-spaced">
          <table className="data-table">
            <thead>
              <tr>
                <th>رقم الوارد</th>
                <th>الموضوع</th>
                <th>الجهة</th>
                <th>ترتيب التعقيب</th>
                <th>تاريخ الطلب</th>
                <th>حالة التسجيل</th>
                <th>إجراءات</th>
              </tr>
            </thead>
            <tbody>
              {records.map((record) => (
                <PendingRecordRow key={record.id} record={record} handlers={handlers} />
              ))}
            </tbody>
          </table>
        </div>
        <Pagination
          page={page}
          pageSize={pageSize}
          total={summaryTotal}
          itemCount={records.length}
          onPageChange={setPage}
          onPageSizeChange={(size) => { setPageSize(size); setPage(1); }}
        />
      </>
    );
  };

  return (
    <div dir="rtl">
      <PageHeader
        title="بانتظار تسجيل التعقيب"
        subtitle="سجلات طُلبت طباعتها ولم تُربط بعد بتعقيب مسجل"
        actions={canClose ? (
          <>
            <Link to="/follow-up-print/jobs" className="btn btn-outline">مهام الطباعة</Link>
            <Link to="/follow-up-print/eligible" className="btn btn-outline">المعاملات المستحقة</Link>
          </>
        ) : undefined}
      />

      {message && <Alert variant="success">{message}</Alert>}
      {error && <Alert variant="error">{error}</Alert>}

      <div className="card">
        <div className="card-header">
          <h3 className="card-title">السجلات بانتظار تسجيل التعقيب</h3>
          <span className="badge badge-orange">{summaryTotal} معلق</span>
        </div>
        {renderRecordsContent()}
      </div>

      {previewHtml && (
        <div className="card mt-4">
          <div className="card-header">
            <h3 className="card-title">عرض الخطاب</h3>
            <button type="button" className="btn btn-outline btn-sm" onClick={() => setPreviewHtml('')}>إغلاق</button>
          </div>
          {previewWarning && <Alert variant="warning">{previewWarning}</Alert>}
          <iframe
            title="عرض الخطاب"
            className="letter-template-preview-frame"
            srcDoc={previewHtml}
            sandbox="allow-same-origin allow-modals"
          />
        </div>
      )}

      {registerDialogRecord && (
        <div className="modal-overlay">
          <dialog open className="modal modal-wide" aria-labelledby="pending-register-dialog-title">
            <h3 id="pending-register-dialog-title">تسجيل التعقيب</h3>
            <p className="text-muted">
              {registerDialogRecord.incomingNumber} — {registerDialogRecord.subject}
            </p>
            <FollowUpFormPanel
              transactionId={registerDialogRecord.transactionId}
              onDirtyChange={() => undefined}
              onCancel={closeRegisterDialog}
              onSuccess={(followUp) => { handleFollowUpRegistered(followUp).catch(() => undefined); }}
            />
          </dialog>
        </div>
      )}

      {linkDialogRecord && (
        <div className="modal-overlay">
          <dialog open className="modal" aria-labelledby="pending-link-dialog-title">
            <h3 id="pending-link-dialog-title">ربط تعقيب موجود</h3>
            <p className="text-muted">
              اختر التعقيب المسجل لهذه المعاملة.
            </p>
            <div className="form-group">
              <div className="text-muted">
                المعاملة: {linkDialogRecord.incomingNumber} - {linkDialogRecord.subject}
              </div>
            </div>

            {linkLoading && <LoadingInline label="جاري تحميل التعقيبات..." />}

            {!linkLoading && linkError && <Alert variant="error">{linkError}</Alert>}

            {!linkLoading && !linkError && linkFollowUps.length === 0 && (
              <Alert variant="info">
                لا توجد تعقيبات مسجلة لهذه المعاملة. استخدم "تسجيل التعقيب" لتسجيل تعقيب جديد وربطه مباشرة.
              </Alert>
            )}

            {!linkLoading && !linkError && linkFollowUps.length > 0 && (
              <div className="modal-followup-list">
                {linkFollowUps.map((followUp) => {
                  const selecting = linkingFollowUpId === followUp.id;
                  return (
                    <div key={followUp.id} className="card" style={{ marginBottom: 'var(--space-3)' }}>
                      <div className="card-header" style={{ alignItems: 'flex-start' }}>
                        <div>
                          <h4 className="card-title">{getFollowUpReference(followUp)}</h4>
                          <div className="text-muted">التاريخ: <DateDisplay date={followUp.followUpDate} /></div>
                          <div className="text-muted">الجهة: {getFollowUpTarget(followUp)}</div>
                          <div className="text-muted">مختصر: {getFollowUpSnippet(followUp)}</div>
                        </div>
                        <button
                          type="button"
                          className="btn btn-primary btn-sm"
                          onClick={() => { handleSelectFollowUp(followUp).catch(() => undefined); }}
                          disabled={selecting || Boolean(linkingFollowUpId)}
                        >
                          {selecting ? 'جاري الربط...' : 'ربط هذا التعقيب'}
                        </button>
                      </div>
                    </div>
                  );
                })}
              </div>
            )}

            <div className="modal-actions">
              <button type="button" className="btn btn-outline" onClick={closeLinkDialog} disabled={linkLoading || Boolean(linkingFollowUpId)}>
                إغلاق
              </button>
            </div>
          </dialog>
        </div>
      )}

      {cancelDialogRecord && (
        <div className="modal-overlay">
          <dialog open className="modal" aria-labelledby="pending-cancel-dialog-title">
            <h3 id="pending-cancel-dialog-title">إلغاء سجل الطباعة</h3>
            <p className="text-muted">
              سيُحذف السجل من قائمة الانتظار. حدّد سبب الإلغاء بوضوح.
            </p>
            <div className="form-group">
              <label htmlFor="cancel-reason">سبب الإلغاء</label>
              <textarea
                id="cancel-reason"
                value={cancelReason}
                onChange={(event) => setCancelReason(event.target.value)}
                rows={4}
                disabled={cancelSubmitting}
              />
            </div>
            {cancelError && <Alert variant="error">{cancelError}</Alert>}
            <div className="modal-actions">
              <button type="button" className="btn btn-primary" onClick={() => { handleSubmitCancel().catch(() => undefined); }} disabled={cancelSubmitting}>
                {cancelSubmitting ? 'جاري الإلغاء...' : 'تأكيد الإلغاء'}
              </button>
              <button type="button" className="btn btn-outline" onClick={closeCancelDialog} disabled={cancelSubmitting}>
                إغلاق
              </button>
            </div>
          </dialog>
        </div>
      )}
    </div>
  );
}
