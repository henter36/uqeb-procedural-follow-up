import { useEffect, useState, type FormEvent } from 'react';
import type { TransactionDetail } from '../../api/types';
import { transactionsApi } from '../../api/services';
import { buildCompleteResponsePayload, getApiErrorMessage } from '../../utils/apiHelpers';
import { FUTURE_EVENT_DATE_MESSAGE, isFutureLocalDate } from '../../utils/localDate';
import { Alert } from '../ui';
import HijriDateInput from '../HijriDateInput';

type FormState = {
  responseDate: string;
  responseSummary: string;
  outgoingNumber: string;
  outgoingDate: string;
};

function fromTransaction(tx: TransactionDetail): FormState {
  return {
    responseDate: tx.responseCompletedDate?.slice(0, 10) ?? '',
    responseSummary: tx.responseSummary ?? '',
    outgoingNumber: tx.outgoingNumber ?? '',
    outgoingDate: tx.outgoingDate?.slice(0, 10) ?? '',
  };
}

type Props = Readonly<{
  transactionId: number;
  transaction: TransactionDetail;
  onDirtyChange: (dirty: boolean) => void;
  onCancel: () => void;
  onSuccess: (updated: TransactionDetail) => void;
}>;

export default function AdminEditTransactionResponseFormPanel({
  transactionId,
  transaction,
  onDirtyChange,
  onCancel,
  onSuccess,
}: Props) {
  const requiresOutgoing = transaction.responseType === 'External' || transaction.responseType === 'Both';
  const [initialForm] = useState<FormState>(() => fromTransaction(transaction));
  const [form, setForm] = useState<FormState>(initialForm);
  const [error, setError] = useState('');
  const [saving, setSaving] = useState(false);

  const dirty = form.responseDate !== initialForm.responseDate
    || form.responseSummary !== initialForm.responseSummary
    || form.outgoingNumber !== initialForm.outgoingNumber
    || form.outgoingDate !== initialForm.outgoingDate;

  useEffect(() => {
    onDirtyChange(dirty);
  }, [dirty, onDirtyChange]);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (saving) return;
    if (!form.responseSummary.trim()) {
      setError('ملخص الإفادة مطلوب');
      return;
    }
    if (isFutureLocalDate(form.responseDate) || isFutureLocalDate(form.outgoingDate)) {
      setError(FUTURE_EVENT_DATE_MESSAGE);
      return;
    }
    if (requiresOutgoing && (!form.outgoingNumber.trim() || !form.outgoingDate)) {
      setError('رقم الصادر وتاريخ الصادر مطلوبان لنوع الإفادة المحدد');
      return;
    }
    setSaving(true);
    setError('');
    try {
      const res = await transactionsApi.editResponse(
        transactionId,
        buildCompleteResponsePayload({ ...form, requiresOutgoing }),
      );
      onSuccess(res.data);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err));
    } finally {
      setSaving(false);
    }
  };

  return (
    <form onSubmit={submit} className="workspace-form">
      {error && <Alert variant="error">{error}</Alert>}
      <p className="text-muted workspace-form-hint">تعديل بيانات الإفادة المسجلة لهذه المعاملة.</p>
      <div className="form-grid">
        <div className="form-group">
          <HijriDateInput
            id="edit-transaction-response-date"
            label="تاريخ الإفادة"
            required
            value={form.responseDate}
            onChange={(responseDate) => setForm((prev) => ({ ...prev, responseDate }))}
            disallowFutureDate
          />
        </div>
        <div className="form-group full-width">
          <label htmlFor="edit-transaction-response-summary">ملخص الإفادة *</label>
          <textarea
            id="edit-transaction-response-summary"
            required
            rows={4}
            value={form.responseSummary}
            onChange={(e) => setForm((prev) => ({ ...prev, responseSummary: e.target.value }))}
          />
        </div>
        {requiresOutgoing && (
          <>
            <div className="form-group">
              <label htmlFor="edit-transaction-outgoing-number">رقم الصادر *</label>
              <input
                id="edit-transaction-outgoing-number"
                required
                value={form.outgoingNumber}
                onChange={(e) => setForm((prev) => ({ ...prev, outgoingNumber: e.target.value }))}
              />
            </div>
            <div className="form-group">
              <HijriDateInput
                id="edit-transaction-outgoing-date"
                label="تاريخ الصادر"
                required
                value={form.outgoingDate}
                onChange={(outgoingDate) => setForm((prev) => ({ ...prev, outgoingDate }))}
                disallowFutureDate
              />
            </div>
          </>
        )}
      </div>
      <div className="form-actions">
        <button type="submit" className="btn btn-primary" disabled={saving || !dirty}>
          {saving ? 'جارٍ الحفظ...' : 'حفظ التعديلات'}
        </button>
        <button type="button" className="btn btn-outline" onClick={onCancel}>إلغاء</button>
      </div>
    </form>
  );
}
