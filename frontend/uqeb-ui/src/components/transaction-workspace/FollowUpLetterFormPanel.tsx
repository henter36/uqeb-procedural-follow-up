import { useCallback, useEffect, useRef, useState, type SyntheticEvent } from 'react';
import type { Assignment, TransactionDetail } from '../../api/types';
import { followUpPrintApi, transactionsApi } from '../../api/services';
import { resolveFollowUpLetterRecipient } from '../../utils/followUpLetter';
import { getApiErrorMessage } from '../../utils/apiHelpers';
import { downloadBlob } from '../../utils/downloadBlob';
import { openHtmlPrintWindow } from '../../utils/followUpPrintWindow';
import { Alert, LoadingInline } from '../ui';
import { createIdempotencyKey } from '../../utils/createIdempotencyKey';
import { usePendingPrintSummary } from '../../hooks/usePendingPrintSummary';

type FollowUpLetterFormPanelProps = Readonly<{
  transactionId: number;
  tx: TransactionDetail;
  assignments: Assignment[];
  onDirtyChange: (dirty: boolean) => void;
  onDownloaded: () => void;
  onCancel: () => void;
}>;

type LetterBaseline = Readonly<{
  recipient: string;
  letterBody: string;
}>;

export default function FollowUpLetterFormPanel({
  transactionId,
  tx,
  assignments,
  onDirtyChange,
  onDownloaded,
  onCancel,
}: FollowUpLetterFormPanelProps) {
  const defaultRecipient = resolveFollowUpLetterRecipient(
    tx.outgoingDepartments,
    assignments,
    tx.incomingFrom,
  );
  const [recipient, setRecipient] = useState(defaultRecipient);
  const [letterBody, setLetterBody] = useState('');
  const [loading, setLoading] = useState(true);
  const [regenerating, setRegenerating] = useState(false);
  const [downloading, setDownloading] = useState(false);
  const [printing, setPrinting] = useState(false);
  const [previewOpen, setPreviewOpen] = useState(false);
  const [error, setError] = useState('');
  const baselineRef = useRef<LetterBaseline>({ recipient: defaultRecipient, letterBody: '' });
  const baselineReadyRef = useRef(false);
  const previewDialogRef = useRef<HTMLDialogElement>(null);
  const closePreviewButtonRef = useRef<HTMLButtonElement>(null);
  const { refresh } = usePendingPrintSummary();

  const closePreview = useCallback(() => {
    setPreviewOpen(false);
  }, []);

  const handlePreviewDialogCancel = useCallback((event: SyntheticEvent) => {
    event.preventDefault();
    closePreview();
  }, [closePreview]);

  useEffect(() => {
    const dialog = previewDialogRef.current;
    if (!dialog) return;
    if (previewOpen && !dialog.open) {
      dialog.showModal();
      closePreviewButtonRef.current?.focus();
    } else if (!previewOpen && dialog.open) {
      dialog.close();
    }
  }, [previewOpen]);

  useEffect(() => {
    let cancelled = false;
    (async () => {
      await Promise.resolve();
      if (cancelled) return;
      if (!baselineReadyRef.current) {
        onDirtyChange(false);
        return;
      }
      onDirtyChange(
        recipient !== baselineRef.current.recipient || letterBody !== baselineRef.current.letterBody,
      );
    })().catch(() => undefined);
    return () => {
      cancelled = true;
    };
  }, [recipient, letterBody, onDirtyChange]);

  const regenerateFromTemplate = useCallback(async () => {
    setError('');
    setRegenerating(true);
    try {
      const res = await transactionsApi.previewFollowUpLetter(transactionId, {
        targetEntity: recipient,
      });
      const nextRecipient = res.data.targetEntity ?? recipient;
      const nextBody = res.data.content;
      setRecipient(nextRecipient);
      setLetterBody(nextBody);
      baselineRef.current = { recipient: nextRecipient, letterBody: nextBody };
      baselineReadyRef.current = true;
      onDirtyChange(false);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err));
    } finally {
      setRegenerating(false);
    }
  }, [onDirtyChange, recipient, transactionId]);

  useEffect(() => {
    let cancelled = false;

    const loadInitial = async () => {
      try {
        const res = await transactionsApi.previewFollowUpLetter(transactionId, {
          targetEntity: defaultRecipient,
        });
        if (cancelled) return;
        const initialRecipient = res.data.targetEntity ?? defaultRecipient;
        const initialBody = res.data.content;
        baselineRef.current = { recipient: initialRecipient, letterBody: initialBody };
        baselineReadyRef.current = true;
        setRecipient(initialRecipient);
        setLetterBody(initialBody);
        onDirtyChange(false);
      } catch (err: unknown) {
        if (!cancelled) setError(getApiErrorMessage(err));
      } finally {
        if (!cancelled) setLoading(false);
      }
    };

    loadInitial().catch((err: unknown) => {
      if (!cancelled) setError(getApiErrorMessage(err));
    });
    return () => { cancelled = true; };
  }, [transactionId, defaultRecipient, onDirtyChange]);

  const handleRegenerateClick = () => {
    const dirty = baselineReadyRef.current
      && (recipient !== baselineRef.current.recipient || letterBody !== baselineRef.current.letterBody);
    if (dirty && !globalThis.confirm('سيتم استبدال التعديلات الحالية بنص جديد من القالب. هل تريد المتابعة؟')) {
      return;
    }
    regenerateFromTemplate().catch((err: unknown) => {
      setError(getApiErrorMessage(err));
    });
  };

  const handleDownload = async () => {
    setError('');
    setDownloading(true);
    try {
      const res = await transactionsApi.downloadFollowUpLetterPdf(transactionId, {
        targetEntity: recipient,
        content: letterBody,
      });
      downloadBlob(res.data, `follow-up-letter-${transactionId}.pdf`);
      baselineRef.current = { recipient, letterBody };
      baselineReadyRef.current = true;
      onDirtyChange(false);
      onDownloaded();
    } catch (err: unknown) {
      setError(getApiErrorMessage(err));
    } finally {
      setDownloading(false);
    }
  };

  const handleDirectPrint = async () => {
    if (printing) return;
    setError('');
    setPrinting(true);
    try {
      await followUpPrintApi.registerDirectPrintRequest(transactionId, {
        targetEntityName: recipient,
        followUpSequence: (tx.followUps?.length ?? 0) + 1,
        responseDeadlineDays: tx.responseDueDays,
        idempotencyKey: createIdempotencyKey(),
      });
      const res = await followUpPrintApi.getTransactionPrintView(transactionId, {
        targetEntity: recipient,
        content: letterBody,
      });
      openHtmlPrintWindow(res.data);
      await refresh();
    } catch (err: unknown) {
      setError(getApiErrorMessage(err));
    } finally {
      setPrinting(false);
    }
  };

  if (loading) return <LoadingInline label="جاري تحميل قالب الخطاب..." />;

  return (
    <div className="workspace-form follow-up-letter-panel" dir="rtl">
      <div className="form-grid">
        <div className="form-group full-width">
          <label htmlFor="letter-recipient">الجهة</label>
          <input
            id="letter-recipient"
            type="text"
            value={recipient}
            onChange={(e) => setRecipient(e.target.value)}
            placeholder="اسم الإدارة أو الجهة"
          />
        </div>
        <div className="form-group full-width">
          <label htmlFor="letter-body">نص الخطاب</label>
          <textarea
            id="letter-body"
            className="follow-up-letter-body"
            rows={14}
            value={letterBody}
            onChange={(e) => setLetterBody(e.target.value)}
          />
        </div>
      </div>
      {error && <Alert variant="error">{error}</Alert>}
      <div className="form-actions">
        <button
          type="button"
          className="btn btn-secondary"
          disabled={!letterBody.trim()}
          onClick={() => setPreviewOpen(true)}
        >
          معاينة
        </button>
        <button
          type="button"
          className="btn btn-secondary"
          disabled={regenerating}
          onClick={handleRegenerateClick}
        >
          {regenerating ? 'جاري التوليد...' : 'إعادة توليد النص من القالب'}
        </button>
        <button
          type="button"
          className="btn btn-secondary"
          disabled={printing || !letterBody.trim()}
          onClick={() => { handleDirectPrint().catch(() => undefined); }}
        >
          {printing ? 'جاري التحضير...' : 'طباعة مباشرة'}
        </button>
        <button type="button" className="btn btn-primary" disabled={downloading || !letterBody.trim()} onClick={() => { handleDownload().catch(() => undefined); }}>
          {downloading ? 'جاري التحميل...' : 'تحميل PDF'}
        </button>
        <button type="button" className="btn btn-outline" onClick={onCancel}>إلغاء</button>
      </div>

      <dialog
        ref={previewDialogRef}
        className="modal follow-up-letter-modal"
        aria-labelledby="follow-up-preview-title"
        onCancel={handlePreviewDialogCancel}
        onClose={closePreview}
      >
        <h3 id="follow-up-preview-title">معاينة الخطاب</h3>
        <div className="form-group">
          <label htmlFor="follow-up-preview-recipient">الجهة</label>
          <p id="follow-up-preview-recipient" className="preview-readonly">{recipient || '—'}</p>
        </div>
        <div className="form-group">
          <label htmlFor="follow-up-preview-body">نص الخطاب</label>
          <pre id="follow-up-preview-body" className="follow-up-letter-preview-body">{letterBody}</pre>
        </div>
        <div className="modal-actions">
          <button
            ref={closePreviewButtonRef}
            type="button"
            className="btn btn-outline"
            onClick={closePreview}
          >
            إغلاق
          </button>
        </div>
      </dialog>
    </div>
  );
}
