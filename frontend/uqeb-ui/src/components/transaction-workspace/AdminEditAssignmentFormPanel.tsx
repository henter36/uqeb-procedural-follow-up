import { useEffect, useState, type FormEvent } from 'react';
import type { Assignment } from '../../api/types';
import { transactionsApi } from '../../api/services';
import { getApiErrorMessage } from '../../utils/apiHelpers';
import { addDaysIso, diffDaysIso, FUTURE_EVENT_DATE_MESSAGE, isFutureLocalDate } from '../../utils/localDate';
import { Alert } from '../ui';
import HijriDateInput from '../HijriDateInput';

type FormState = {
  letterNumber: string;
  assignedDate: string;
  requiredAction: string;
  replyDueDays: string;
  dueDate: string;
};

function fromAssignment(a?: Assignment, fallbackLetterNumber = ''): FormState {
  const hasReplyDueDays = a?.replyDueDays != null;

  return {
    letterNumber: a?.letterNumber ?? fallbackLetterNumber,
    assignedDate: a?.assignedDate?.slice(0, 10) ?? '',
    requiredAction: a?.requiredAction ?? '',
    replyDueDays: hasReplyDueDays ? String(a.replyDueDays) : '',
    dueDate: a?.dueDate?.slice(0, 10) ?? '',
  };
}

function isDirty(current: FormState, initial: FormState): boolean {
  return (Object.keys(current) as (keyof FormState)[]).some((k) => current[k] !== initial[k]);
}

type Props = Readonly<{
  transactionId: number;
  assignmentId: number;
  initialAssignment?: Assignment;
  fallbackLetterNumber?: string | null;
  onDirtyChange: (dirty: boolean) => void;
  onCancel: () => void;
  onSuccess: (updated: Assignment) => void;
}>;

export default function AdminEditAssignmentFormPanel({
  transactionId,
  assignmentId,
  initialAssignment,
  fallbackLetterNumber,
  onDirtyChange,
  onCancel,
  onSuccess,
}: Props) {
  const [initialForm] = useState<FormState>(() => fromAssignment(initialAssignment, fallbackLetterNumber?.trim() ?? ''));
  const [form, setForm] = useState<FormState>(initialForm);
  const [error, setError] = useState('');
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    onDirtyChange(isDirty(form, initialForm));
  }, [form, initialForm, onDirtyChange]);

  const updateAssignedDate = (assignedDate: string) => setForm((prev) => {
    if (!assignedDate) {
      return { ...prev, assignedDate, dueDate: '', replyDueDays: '' };
    }
    return {
      ...prev,
      assignedDate,
      dueDate: prev.replyDueDays
        ? addDaysIso(assignedDate, Number(prev.replyDueDays))
        : prev.dueDate,
    };
  });

  const updateReplyDueDays = (replyDueDays: string) => setForm((prev) => ({
    ...prev,
    replyDueDays,
    dueDate: prev.assignedDate && replyDueDays
      ? addDaysIso(prev.assignedDate, Number(replyDueDays))
      : '',
  }));

  const updateDueDate = (dueDate: string) => setForm((prev) => {
    if (!prev.assignedDate || !dueDate) {
      return { ...prev, dueDate, replyDueDays: '' };
    }
    const diff = diffDaysIso(prev.assignedDate, dueDate);
    return {
      ...prev,
      dueDate,
      replyDueDays: Number.isFinite(diff) ? String(diff) : prev.replyDueDays,
    };
  });

  const update = (patch: Partial<FormState>) => setForm((prev) => ({ ...prev, ...patch }));

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (saving) return;
    if (isFutureLocalDate(form.assignedDate)) {
      setError(FUTURE_EVENT_DATE_MESSAGE);
      return;
    }
    if (form.replyDueDays && Number(form.replyDueDays) < 0) {
      setError('عدد أيام الرد لا يمكن أن يكون سالبًا.');
      return;
    }
    if (form.assignedDate && form.dueDate && form.dueDate < form.assignedDate) {
      setError('تاريخ استحقاق الإدارة لا يمكن أن يسبق تاريخ الإحالة.');
      return;
    }
    setSaving(true);
    setError('');
    try {
      const res = await transactionsApi.adminEditAssignment(transactionId, assignmentId, {
        letterNumber: form.letterNumber.trim() || null,
        assignedDate: form.assignedDate || null,
        requiredAction: form.requiredAction.trim() || null,
        replyDueDays: form.replyDueDays ? Number(form.replyDueDays) : null,
        dueDate: form.dueDate || null,
      });
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
      <div className="form-grid">
        <div className="form-group">
          <label htmlFor="admin-edit-letter">رقم الخطاب</label>
          <input
            id="admin-edit-letter"
            value={form.letterNumber}
            onChange={(e) => update({ letterNumber: e.target.value })}
          />
        </div>
        <div className="form-group">
          <HijriDateInput
            id="admin-edit-assigned-date"
            label="تاريخ الإحالة"
            value={form.assignedDate}
            onChange={updateAssignedDate}
            disallowFutureDate
          />
        </div>
        <div className="form-group full-width">
          <label htmlFor="admin-edit-action">الإجراء المطلوب</label>
          <input
            id="admin-edit-action"
            value={form.requiredAction}
            onChange={(e) => update({ requiredAction: e.target.value })}
          />
        </div>
        <div className="form-group">
          <label htmlFor="admin-edit-days">عدد أيام الرد</label>
          <input
            id="admin-edit-days"
            type="number"
            min="0"
            value={form.replyDueDays}
            onChange={(e) => updateReplyDueDays(e.target.value)}
          />
        </div>
        <div className="form-group">
          <HijriDateInput
            id="admin-edit-due"
            label="تاريخ الاستحقاق"
            value={form.dueDate}
            onChange={updateDueDate}
          />
        </div>
      </div>
      <div className="form-actions">
        <button type="submit" className="btn btn-primary" disabled={saving}>
          {saving ? 'جارٍ الحفظ...' : 'حفظ التعديلات'}
        </button>
        <button type="button" className="btn btn-outline" onClick={onCancel}>إلغاء</button>
      </div>
    </form>
  );
}
