import { useEffect, useState, type FormEvent } from 'react';
import { buildReplyPayload, getApiErrorMessage } from '../../utils/apiHelpers';
import { todayLocalIso } from '../../utils/localDate';
import { Alert } from '../ui';
import HijriDateInput from '../HijriDateInput';

type ReplyFormPanelProps = Readonly<{
  title: string;
  onDirtyChange: (dirty: boolean) => void;
  onSubmit: (payload: ReturnType<typeof buildReplyPayload>) => Promise<unknown>;
  onSuccess: () => void;
  onCancel: () => void;
}>;

export default function ReplyFormPanel({
  title,
  onDirtyChange,
  onSubmit,
  onSuccess,
  onCancel,
}: ReplyFormPanelProps) {
  const [form, setForm] = useState({ replyDate: todayLocalIso(), replySummary: '' });
  const [error, setError] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);

  useEffect(() => {
    onDirtyChange(Boolean(form.replySummary.trim()));
  }, [form.replySummary, onDirtyChange]);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (isSubmitting) return;
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
    <form onSubmit={submit} className="workspace-form" aria-label={title}>
      {error && <Alert variant="error">{error}</Alert>}
      <div className="form-grid">
        <div className="form-group">
          <HijriDateInput
            id="reply-date"
            label="تاريخ الرد"
            required
            value={form.replyDate}
            onChange={(replyDate) => setForm({ ...form, replyDate })}
          />
        </div>
        <div className="form-group full-width">
          <label htmlFor="reply-summary">ملخص الرد *</label>
          <textarea id="reply-summary" required rows={4} value={form.replySummary} onChange={(e) => setForm({ ...form, replySummary: e.target.value })} />
        </div>
      </div>
      <div className="form-actions">
        <button type="submit" className="btn btn-primary" disabled={isSubmitting}>
          {isSubmitting ? 'جاري الحفظ...' : 'حفظ الرد'}
        </button>
        <button type="button" className="btn btn-outline" onClick={onCancel}>إلغاء</button>
      </div>
    </form>
  );
}
