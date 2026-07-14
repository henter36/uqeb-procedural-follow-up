import { useEffect, useState, type FormEvent } from 'react';
import type { TransactionDetail } from '../../api/types';
import { transactionsApi } from '../../api/services';
import { getApiErrorMessage } from '../../utils/apiHelpers';
import { addDaysIso, diffDaysIso, FUTURE_EVENT_DATE_MESSAGE, isFutureLocalDate } from '../../utils/localDate';
import { Alert } from '../ui';
import HijriDateInput from '../HijriDateInput';
import { AdminEditAuditHint, AdminEditFormActions, AdminEditReasonField } from './AdminEditFormShared';

type FormState = {
  incomingDate: string;
  responseDueDays: string;
  responseDueDate: string;
  closedAt: string;
  reason: string;
};

function fromTransaction(tx?: TransactionDetail): FormState {
  return {
    incomingDate: tx?.incomingDate?.slice(0, 10) ?? '',
    responseDueDays: tx?.responseDueDays != null ? String(tx.responseDueDays) : '',
    responseDueDate: tx?.responseDueDate?.slice(0, 10) ?? '',
    closedAt: tx?.closedAt?.slice(0, 10) ?? '',
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
    || form.responseDueDays !== initialForm.responseDueDays
    || form.responseDueDate !== initialForm.responseDueDate
    || form.closedAt !== initialForm.closedAt;

  useEffect(() => {
    onDirtyChange(dirty);
  }, [dirty, onDirtyChange]);

  const updateIncomingDate = (incomingDate: string) => setForm((prev) => {
    if (!incomingDate) {
      return { ...prev, incomingDate, responseDueDate: '', responseDueDays: '' };
    }
    return {
      ...prev,
      incomingDate,
      responseDueDate: prev.responseDueDays
        ? addDaysIso(incomingDate, Number(prev.responseDueDays))
        : prev.responseDueDate,
    };
  });

  const updateResponseDueDays = (responseDueDays: string) => setForm((prev) => {
    if (!responseDueDays) {
      return { ...prev, responseDueDays, responseDueDate: '' };
    }
    const days = Number(responseDueDays);
    if (!prev.incomingDate || !Number.isFinite(days)) {
      return { ...prev, responseDueDays };
    }
    return { ...prev, responseDueDays, responseDueDate: addDaysIso(prev.incomingDate, days) };
  });

  const updateResponseDueDate = (responseDueDate: string) => setForm((prev) => {
    if (!prev.incomingDate || !responseDueDate) {
      return { ...prev, responseDueDate, responseDueDays: '' };
    }
    const diff = diffDaysIso(prev.incomingDate, responseDueDate);
    return {
      ...prev,
      responseDueDate,
      responseDueDays: Number.isFinite(diff) ? String(diff) : prev.responseDueDays,
    };
  });

  const update = (patch: Partial<FormState>) => setForm((prev) => ({ ...prev, ...patch }));

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (saving) return;
    if (!form.reason.trim()) {
      setError('سبب التعديل مطلوب للحقول الزمنية الحساسة.');
      return;
    }
    if (isFutureLocalDate(form.incomingDate) || isFutureLocalDate(form.closedAt)) {
      setError(FUTURE_EVENT_DATE_MESSAGE);
      return;
    }
    if (form.responseDueDays) {
      const days = Number(form.responseDueDays);
      if (!Number.isFinite(days) || days < 0) {
        setError('عدد أيام الرد لا يمكن أن يكون سالبًا.');
        return;
      }
    }
    if (form.incomingDate && form.responseDueDate && form.responseDueDate < form.incomingDate) {
      setError('تاريخ استحقاق المعاملة لا يمكن أن يسبق تاريخ الوارد.');
      return;
    }
    if (form.incomingDate && form.closedAt && form.closedAt < form.incomingDate) {
      setError('تاريخ إغلاق المعاملة لا يمكن أن يسبق تاريخ الوارد.');
      return;
    }
    setSaving(true);
    setError('');
    try {
      const payload: Record<string, unknown> = {
        reason: form.reason.trim(),
        incomingDate: form.incomingDate || null,
        responseDueDays: form.responseDueDays ? Number(form.responseDueDays) : null,
        responseDueDate: form.responseDueDate || null,
        closedAt: form.closedAt || null,
      };
      const res = await transactionsApi.adminEditTransactionDates(transactionId, payload);
      onDirtyChange(false);
      onSuccess(res.data);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err));
    } finally {
      setSaving(false);
    }
  };

  return (
    <form onSubmit={submit} className="workspace-form">
      <AdminEditAuditHint />
      {error && <Alert variant="error">{error}</Alert>}
      <div className="form-grid">
        <div className="form-group">
          <HijriDateInput
            id="admin-dates-incoming"
            label="تاريخ الوارد"
            value={form.incomingDate}
            onChange={updateIncomingDate}
            disallowFutureDate
          />
          <small className="text-muted">بداية احتساب عمر المعاملة وأيام الإنجاز.</small>
        </div>
        <div className="form-group">
          <label htmlFor="admin-dates-due-days">عدد أيام الرد</label>
          <input
            id="admin-dates-due-days"
            type="number"
            min="0"
            value={form.responseDueDays}
            onChange={(e) => updateResponseDueDays(e.target.value)}
          />
          <small className="text-muted">تغيير العدد يعيد حساب تاريخ الاستحقاق من تاريخ الوارد.</small>
        </div>
        <div className="form-group">
          <HijriDateInput
            id="admin-dates-due"
            label="تاريخ استحقاق المعاملة"
            value={form.responseDueDate}
            onChange={updateResponseDueDate}
          />
          <small className="text-muted">تغيير التاريخ يعيد حساب عدد الأيام من تاريخ الوارد.</small>
        </div>
        <div className="form-group">
          <HijriDateInput
            id="admin-dates-closed"
            label="تاريخ إغلاق المعاملة"
            value={form.closedAt}
            onChange={(closedAt) => update({ closedAt })}
            disallowFutureDate
          />
          <small className="text-muted">يُستخدم لحساب أيام إنجاز المعاملة. لا يسبق تاريخ الوارد.</small>
        </div>
        <AdminEditReasonField
          id="admin-dates-reason"
          value={form.reason}
          onChange={(reason) => update({ reason })}
        />
      </div>
      <AdminEditFormActions saving={saving} dirty={dirty} reason={form.reason} onCancel={onCancel} />
    </form>
  );
}
