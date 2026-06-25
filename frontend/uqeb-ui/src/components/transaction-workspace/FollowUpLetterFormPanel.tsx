import { useCallback, useEffect, useRef, useState } from 'react';
import type { Assignment, TransactionDetail } from '../../api/types';
import { followUpPrintApi, transactionsApi } from '../../api/services';
import { resolveFollowUpLetterRecipient } from '../../utils/followUpLetter';
import { getApiErrorMessage } from '../../utils/apiHelpers';
import { downloadBlob } from '../../utils/downloadBlob';
import { openHtmlPrintWindow } from '../../utils/followUpPrintWindow';
import { Alert, LoadingInline } from '../ui';

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

  useEffect(() => {
    let cancelled = false;
    void (async () => {
      await Promise.resolve();
      if (cancelled) return;
      if (!baselineReadyRef.current) {
        onDirtyChange(false);
        return;
      }
      onDirtyChange(
        recipient !== baselineRef.current.recipient || letterBody !== baselineRef.current.letterBody,
      );
    })();
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
    if (dirty && !window.confirm('سيتم استبدال التعديلات الحالية بنص جديد من القالب. هل تريد المتابعة؟')) {
      return;
    }
    void regenerateFromTemplate();
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
    setError('');
    setPrinting(true);
    try {
      const res = await followUpPrintApi.getTransactionPrintView(transactionId, {
        targetEntity: recipient,
        content: letterBody,
      });
      openHtmlPrintWindow(res.data);
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
          onClick={() => { void handleDirectPrint(); }}
        >
          {printing ? 'جاري التحضير...' : 'طباعة مباشرة'}
        </button>
        <button type="button" className="btn btn-primary" disabled={downloading || !letterBody.trim()} onClick={() => { void handleDownload(); }}>
          {downloading ? 'جاري التحميل...' : 'تحميل PDF'}
        </button>
        <button type="button" className="btn btn-outline" onClick={onCancel}>إلغاء</button>
      </div>

      {previewOpen && (
        <div className="modal-overlay" role="presentation" onClick={() => setPreviewOpen(false)}>
          <div
            className="modal follow-up-letter-modal"
            role="dialog"
            aria-modal="true"
            aria-labelledby="follow-up-preview-title"
            onClick={(e) => e.stopPropagation()}
          >
            <h3 id="follow-up-preview-title">معاينة الخطاب</h3>
            <div className="form-group">
              <label>الجهة</label>
              <p className="preview-readonly">{recipient || '—'}</p>
            </div>
            <div className="form-group">
              <label>نص الخطاب</label>
              <pre className="follow-up-letter-preview-body">{letterBody}</pre>
            </div>
            <div className="modal-actions">
              <button type="button" className="btn btn-outline" onClick={() => setPreviewOpen(false)}>
                إغلاق
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
