import { useCallback, useEffect, useState } from 'react';
import type { Assignment, TransactionDetail } from '../../api/types';
import { transactionsApi } from '../../api/services';
import { resolveFollowUpLetterRecipient } from '../../utils/followUpLetter';
import { getApiErrorMessage } from '../../utils/apiHelpers';
import { Alert, LoadingInline } from '../ui';

type FollowUpLetterFormPanelProps = Readonly<{
  transactionId: number;
  tx: TransactionDetail;
  assignments: Assignment[];
  onDirtyChange: (dirty: boolean) => void;
  onDownloaded: () => void;
  onCancel: () => void;
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
  const [previewing, setPreviewing] = useState(false);
  const [downloading, setDownloading] = useState(false);
  const [error, setError] = useState('');

  useEffect(() => {
    onDirtyChange(Boolean(letterBody.trim() && letterBody !== defaultRecipient));
  }, [letterBody, defaultRecipient, onDirtyChange]);

  const loadPreview = useCallback(async (targetEntity?: string, keepEditedContent = false) => {
    setError('');
    setPreviewing(true);
    try {
      const res = await transactionsApi.previewFollowUpLetter(transactionId, {
        targetEntity: targetEntity ?? recipient,
        ...(keepEditedContent && letterBody.trim() ? { content: letterBody } : {}),
      });
      setLetterBody(res.data.content);
      if (res.data.targetEntity) setRecipient(res.data.targetEntity);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err));
    } finally {
      setPreviewing(false);
      setLoading(false);
    }
  }, [transactionId, recipient, letterBody]);

  useEffect(() => {
    let cancelled = false;
    transactionsApi.previewFollowUpLetter(transactionId, { targetEntity: defaultRecipient })
      .then((res) => {
        if (cancelled) return;
        setLetterBody(res.data.content);
        if (res.data.targetEntity) setRecipient(res.data.targetEntity);
      })
      .catch((err: unknown) => {
        if (!cancelled) setError(getApiErrorMessage(err));
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => { cancelled = true; };
  }, [transactionId, defaultRecipient]);

  const handleDownload = async () => {
    setError('');
    setDownloading(true);
    try {
      const res = await transactionsApi.downloadFollowUpLetterPdf(transactionId, {
        targetEntity: recipient,
        content: letterBody,
      });
      const url = window.URL.createObjectURL(res.data);
      const a = document.createElement('a');
      a.href = url;
      a.download = `follow-up-letter-${transactionId}.pdf`;
      a.click();
      window.URL.revokeObjectURL(url);
      onDownloaded();
    } catch (err: unknown) {
      setError(getApiErrorMessage(err));
    } finally {
      setDownloading(false);
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
        <button type="button" className="btn btn-secondary" disabled={previewing} onClick={() => loadPreview(recipient, false)}>
          {previewing ? 'جاري المعاينة...' : 'معاينة'}
        </button>
        <button type="button" className="btn btn-primary" disabled={downloading || !letterBody.trim()} onClick={handleDownload}>
          {downloading ? 'جاري التحميل...' : 'تحميل PDF'}
        </button>
        <button type="button" className="btn btn-outline" onClick={onCancel}>إلغاء</button>
      </div>
    </div>
  );
}
