import { useEffect, useRef, useState, type FormEvent } from 'react';
import { transactionsApi } from '../../api/services';
import { buildCompleteResponsePayload, getApiErrorMessage } from '../../utils/apiHelpers';
import { todayLocalIso } from '../../utils/localDate';
import { Alert } from '../ui';
import HijriDateInput from '../HijriDateInput';

export type CompleteResponseSuccessResult = Readonly<{
  attachmentWarning?: string;
}>;

type CompleteResponseFormPanelProps = Readonly<{
  transactionId: number;
  responseType: string;
  onDirtyChange: (dirty: boolean) => void;
  onSuccess: (result?: CompleteResponseSuccessResult) => void;
  onCancel: () => void;
}>;

const ATTACHMENT_PARTIAL_WARNING = 'تم تسجيل الإفادة، لكن تعذر رفع المرفق. يمكنك رفعه من قسم المرفقات.';

export default function CompleteResponseFormPanel({
  transactionId,
  responseType,
  onDirtyChange,
  onSuccess,
  onCancel,
}: CompleteResponseFormPanelProps) {
  const requiresOutgoing = responseType === 'External' || responseType === 'Both';
  const [form, setForm] = useState({
    responseDate: todayLocalIso(),
    responseSummary: '',
    outgoingNumber: '',
    outgoingDate: todayLocalIso(),
  });
  const [attachment, setAttachment] = useState<File | null>(null);
  const [error, setError] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [responseSaved, setResponseSaved] = useState(false);
  const responseSavedRef = useRef(false);

  useEffect(() => {
    const dirty = Boolean(
      form.responseSummary.trim()
      || form.outgoingNumber.trim()
      || attachment,
    );
    onDirtyChange(dirty && !responseSaved);
  }, [form, attachment, onDirtyChange, responseSaved]);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (isSubmitting) return;
    if (!responseSaved) {
      if (!form.responseSummary.trim()) {
        setError('ملخص الإفادة مطلوب');
        return;
      }
      if (requiresOutgoing && (!form.outgoingNumber.trim() || !form.outgoingDate)) {
        setError('رقم الصادر وتاريخ الصادر مطلوبان لنوع الإفادة المحدد');
        return;
      }
    } else if (!attachment) {
      return;
    }

    setError('');
    setIsSubmitting(true);
    try {
      if (!responseSavedRef.current) {
        await transactionsApi.completeResponse(
          transactionId,
          buildCompleteResponsePayload({ ...form, requiresOutgoing }),
        );
        responseSavedRef.current = true;
        setResponseSaved(true);
        onDirtyChange(false);
      }

      if (attachment) {
        try {
          await transactionsApi.uploadAttachment(transactionId, attachment, 'Response');
          onSuccess();
        } catch {
          setAttachment(null);
          onSuccess({ attachmentWarning: ATTACHMENT_PARTIAL_WARNING });
        }
      } else {
        onSuccess();
      }
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
          <HijriDateInput
            id="response-date"
            label="تاريخ الإفادة"
            required
            value={form.responseDate}
            disabled={responseSaved}
            onChange={(responseDate) => setForm({ ...form, responseDate })}
          />
        </div>
        <div className="form-group full-width">
          <label htmlFor="response-summary">ملخص الإفادة *</label>
          <textarea
            id="response-summary"
            required
            rows={7}
            value={form.responseSummary}
            disabled={responseSaved}
            onChange={(e) => setForm({ ...form, responseSummary: e.target.value })}
          />
        </div>
        {requiresOutgoing && (
          <>
            <div className="form-group">
              <label htmlFor="outgoing-number">رقم الصادر *</label>
              <input
                id="outgoing-number"
                required
                value={form.outgoingNumber}
                disabled={responseSaved}
                onChange={(e) => setForm({ ...form, outgoingNumber: e.target.value })}
              />
            </div>
            <div className="form-group">
              <HijriDateInput
                id="outgoing-date"
                label="تاريخ الصادر"
                required
                value={form.outgoingDate}
                disabled={responseSaved}
                onChange={(outgoingDate) => setForm({ ...form, outgoingDate })}
              />
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
          {isSubmitting ? 'جاري الحفظ...' : 'إرسال الإفادة'}
        </button>
        <button type="button" className="btn btn-outline" onClick={onCancel}>إلغاء</button>
      </div>
    </form>
  );
}
