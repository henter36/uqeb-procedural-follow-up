import { useEffect, useState, type FormEvent } from 'react';
import type { TransactionDetail } from '../../api/types';
import { transactionsApi } from '../../api/services';
import { getApiErrorMessage } from '../../utils/apiHelpers';
import { Alert } from '../ui';
import HijriDateInput from '../HijriDateInput';

type FormState = {
  incomingDate: string;
  responseDueDate: string;
  completionDate: string;
  reason: string;
};

function fromTransaction(tx?: TransactionDetail): FormState {
  return {
    incomingDate: tx?.incomingDate?.slice(0, 10) ?? '',
    responseDueDate: tx?.responseDueDate?.slice(0, 10) ?? '',
    completionDate: tx?.completionDate?.slice(0, 10) ?? '',
    reason: '',
  };
}

type Props = Readonly<{
  transactionId: number;
  transaction?: TransactionDetail;
  onDirtyChange: (dirty: boolean) => void;
  onCancel: () => void;
  onSuccess: (updated: TransactionDetail) => void;
}>;

export default function AdminEditDatesFormPanel({
  transactionId,
  transaction,
  onDirtyChange,
  onCancel,
  onSuccess,
}: Props) {
  const [initialForm] = useState<FormState>(() => fromTransaction(transaction));
  const [form, setForm] = useState<FormState>(initialForm);
  const [error, setError] = useState('');
  const [saving, setSaving] = useState(false);

  const dirty = form.incomingDate !== initialForm.incomingDate
    || form.responseDueDate !== initialForm.responseDueDate
    || form.completionDate !== initialForm.completionDate;

  useEffect(() => {
    onDirtyChange(dirty && form.reason.trim().length > 0);
  }, [dirty, form.reason, onDirtyChange]);

  const update = (patch: Partial<FormState>) => setForm((prev) => ({ ...prev, ...patch }));

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (saving) return;
    if (!form.reason.trim()) {
      setError('سبب التعديل مطلوب للحقول الزمنية الحساسة.');
      return;
    }
    setSaving(true);
    setError('');
    try {
      const payload: Record<string, unknown> = { reason: form.reason.trim() };
      if (form.incomingDate) payload.incomingDate = form.incomingDate;
      if (form.responseDueDate) payload.responseDueDate = form.responseDueDate;
      if (form.completionDate) payload.closedAt = form.completionDate;
      const res = await transactionsApi.adminEditTransactionDates(transactionId, payload);
      onSuccess(res.data);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err));
    } finally {
      setSaving(false);
    }
  };

  return (
    <form onSubmit={submit} className="workspace-form">
      <p className="text-muted workspace-form-hint">
        هذا النموذج للتصحيح الإداري فقط. كل تعديل يُسجَّل في سجل التدقيق مع السبب.
      </p>
      {error && <Alert variant="error">{error}</Alert>}
      <div className="form-grid">
        <div className="form-group">
          <HijriDateInput
            id="admin-dates-incoming"
            label="تاريخ الوارد"
            value={form.incomingDate}
            onChange={(incomingDate) => update({ incomingDate })}
          />
          <small className="text-muted">بداية احتساب عمر المعاملة وأيام الإنجاز.</small>
        </div>
        <div className="form-group">
          <HijriDateInput
            id="admin-dates-due"
            label="تاريخ استحقاق المعاملة"
            value={form.responseDueDate}
            onChange={(responseDueDate) => update({ responseDueDate })}
          />
          <small className="text-muted">لا يسبق تاريخ الوارد.</small>
        </div>
        <div className="form-group">
          <HijriDateInput
            id="admin-dates-closed"
            label="تاريخ إغلاق المعاملة"
            value={form.completionDate}
            onChange={(completionDate) => update({ completionDate })}
          />
          <small className="text-muted">يُستخدم لحساب أيام إنجاز المعاملة. لا يسبق تاريخ الوارد.</small>
        </div>
        <div className="form-group full-width">
          <label htmlFor="admin-dates-reason">سبب التعديل *</label>
          <input
            id="admin-dates-reason"
            required
            value={form.reason}
            onChange={(e) => update({ reason: e.target.value })}
            placeholder="أدخل سبب التصحيح الإداري..."
          />
        </div>
      </div>
      <div className="form-actions">
        <button type="submit" className="btn btn-primary" disabled={saving}>
          {saving ? 'جارٍ الحفظ...' : 'حفظ التصحيح'}
        </button>
        <button type="button" className="btn btn-outline" onClick={onCancel}>إلغاء</button>
      </div>
    </form>
  );
}
