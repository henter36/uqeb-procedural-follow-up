import { useEffect, useState, type FormEvent } from 'react';
import { buildReplyPayload, getApiErrorMessage } from '../../utils/apiHelpers';
import { Alert } from '../ui';
import HijriDateInput from '../HijriDateInput';

type ReplyFormPanelProps = Readonly<{
  title: string;
  dateLabel?: string;
  dateHint?: string;
  dateRequiredMessage?: string;
  summaryLabel?: string;
  submitLabel?: string;
  onDirtyChange: (dirty: boolean) => void;
  onSubmit: (payload: ReturnType<typeof buildReplyPayload>) => Promise<unknown>;
  onSuccess: () => void;
  onCancel: () => void;
}>;

export default function ReplyFormPanel({
  title,
  dateLabel = 'تاريخ الرد',
  dateHint,
  dateRequiredMessage = 'تاريخ الرد مطلوب.',
  summaryLabel = 'ملخص الرد *',
  submitLabel = 'حفظ الرد',
  onDirtyChange,
  onSubmit,
  onSuccess,
  onCancel,
}: ReplyFormPanelProps) {
  const [form, setForm] = useState({ replyDate: '', replySummary: '' });
  const [error, setError] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);

  useEffect(() => {
    onDirtyChange(Boolean(form.replyDate || form.replySummary.trim()));
  }, [form.replyDate, form.replySummary, onDirtyChange]);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (isSubmitting) return;
    if (!form.replyDate) {
      setError(dateRequiredMessage);
      return;
    }
    setError('');
    setIsSubmitting(true);
    try {
      await onSubmit(buildReplyPayload(form));
      onSuccess();
    } catch (err: unknown) {
      setError(getApiErrorMessage(err));
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <form onSubmit={submit} className="workspace-form" aria-label={title} noValidate>
      {error && <Alert variant="error">{error}</Alert>}
      <div className="form-grid">
        <div className="form-group">
          <HijriDateInput
            id="reply-date"
            label={dateLabel}
            required
            value={form.replyDate}
            onChange={(replyDate) => setForm({ ...form, replyDate })}
          />
          {dateHint && <small className="text-muted">{dateHint}</small>}
        </div>
        <div className="form-group full-width">
          <label htmlFor="reply-summary">{summaryLabel}</label>
          <textarea id="reply-summary" required rows={4} value={form.replySummary} onChange={(e) => setForm({ ...form, replySummary: e.target.value })} />
        </div>
      </div>
      <div className="form-actions">
        <button type="submit" className="btn btn-primary" disabled={isSubmitting}>
          {isSubmitting ? 'جاري الحفظ...' : submitLabel}
        </button>
        <button type="button" className="btn btn-outline" onClick={onCancel}>إلغاء</button>
      </div>
    </form>
  );
}
