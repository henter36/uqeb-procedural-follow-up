import { useEffect, useState, type FormEvent } from 'react';
import { transactionsApi } from '../../api/services';
import { buildCompleteResponsePayload, getApiErrorMessage } from '../../utils/apiHelpers';
import { Alert } from '../ui';

type CompleteResponseFormPanelProps = Readonly<{
  transactionId: number;
  responseType: string;
  onDirtyChange: (dirty: boolean) => void;
  onSuccess: () => void;
  onCancel: () => void;
}>;

export default function CompleteResponseFormPanel({
  transactionId,
  responseType,
  onDirtyChange,
  onSuccess,
  onCancel,
}: CompleteResponseFormPanelProps) {
  const requiresOutgoing = responseType === 'External' || responseType === 'Both';
  const [form, setForm] = useState({
    responseDate: new Date().toISOString().split('T')[0],
    responseSummary: '',
    outgoingNumber: '',
    outgoingDate: new Date().toISOString().split('T')[0],
  });
  const [attachment, setAttachment] = useState<File | null>(null);
  const [error, setError] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);

  useEffect(() => {
    const dirty = Boolean(
      form.responseSummary.trim()
      || form.outgoingNumber.trim()
      || attachment,
    );
    onDirtyChange(dirty);
  }, [form, attachment, onDirtyChange]);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (isSubmitting) return;
    if (!form.responseSummary.trim()) {
      setError('ملخص الإفادة مطلوب');
      return;
    }
    if (requiresOutgoing && (!form.outgoingNumber.trim() || !form.outgoingDate)) {
      setError('رقم الصادر وتاريخ الصادر مطلوبان لنوع الإفادة المحدد');
      return;
    }
    setError('');
    setIsSubmitting(true);
    try {
      await transactionsApi.completeResponse(
        transactionId,
        buildCompleteResponsePayload({ ...form, requiresOutgoing }),
      );
      if (attachment) {
        await transactionsApi.uploadAttachment(transactionId, attachment, 'Response');
      }
      onSuccess();
    } catch (err: unknown) {
      setError(getApiErrorMessage(err));
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <form onSubmit={submit} className="workspace-form">
      {error && <Alert variant="error">{error}</Alert>}
      <div className="form-grid">
        <div className="form-group">
          <label htmlFor="response-date">تاريخ الإفادة *</label>
          <input id="response-date" type="date" required value={form.responseDate} onChange={(e) => setForm({ ...form, responseDate: e.target.value })} />
        </div>
        <div className="form-group full-width">
          <label htmlFor="response-summary">ملخص الإفادة *</label>
          <textarea id="response-summary" required rows={4} value={form.responseSummary} onChange={(e) => setForm({ ...form, responseSummary: e.target.value })} />
        </div>
        {requiresOutgoing && (
          <>
            <div className="form-group">
              <label htmlFor="outgoing-number">رقم الصادر *</label>
              <input id="outgoing-number" required value={form.outgoingNumber} onChange={(e) => setForm({ ...form, outgoingNumber: e.target.value })} />
            </div>
            <div className="form-group">
              <label htmlFor="outgoing-date">تاريخ الصادر *</label>
              <input id="outgoing-date" type="date" required value={form.outgoingDate} onChange={(e) => setForm({ ...form, outgoingDate: e.target.value })} />
            </div>
          </>
        )}
        <div className="form-group full-width">
          <label htmlFor="response-attachment">مرفق (اختياري)</label>
          <input id="response-attachment" type="file" onChange={(e) => setAttachment(e.target.files?.[0] ?? null)} />
        </div>
      </div>
      <div className="form-actions">
        <button type="submit" className="btn btn-primary" disabled={isSubmitting}>
          {isSubmitting ? 'جاري الحفظ...' : 'تسجيل الإفادة'}
        </button>
        <button type="button" className="btn btn-outline" onClick={onCancel}>إلغاء</button>
      </div>
    </form>
  );
}
