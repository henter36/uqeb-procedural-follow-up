import { useEffect, useState, type FormEvent } from 'react';
import type { Assignment } from '../../api/types';
import { transactionsApi } from '../../api/services';
import { getApiErrorMessage } from '../../utils/apiHelpers';
import { Alert } from '../ui';
import HijriDateInput from '../HijriDateInput';

type FormState = {
  letterNumber: string;
  assignedDate: string;
  requiredAction: string;
  replyDueDays: string;
  dueDate: string;
};

function fromAssignment(a?: Assignment): FormState {
  return {
    letterNumber: a?.letterNumber ?? '',
    assignedDate: a?.assignedDate?.slice(0, 10) ?? '',
    requiredAction: a?.requiredAction ?? '',
    replyDueDays: a?.replyDueDays != null ? String(a.replyDueDays) : '',
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
  onDirtyChange: (dirty: boolean) => void;
  onCancel: () => void;
  onSuccess: (updated: Assignment) => void;
}>;

export default function AdminEditAssignmentFormPanel({
  transactionId,
  assignmentId,
  initialAssignment,
  onDirtyChange,
  onCancel,
  onSuccess,
}: Props) {
  const [initialForm] = useState<FormState>(() => fromAssignment(initialAssignment));
  const [form, setForm] = useState<FormState>(initialForm);
  const [error, setError] = useState('');
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    onDirtyChange(isDirty(form, initialForm));
  }, [form, initialForm, onDirtyChange]);

  const update = (patch: Partial<FormState>) => setForm((prev) => ({ ...prev, ...patch }));

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (saving) return;
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
            onChange={(assignedDate) => update({ assignedDate })}
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
            min="1"
            value={form.replyDueDays}
            onChange={(e) => update({ replyDueDays: e.target.value })}
          />
        </div>
        <div className="form-group">
          <HijriDateInput
            id="admin-edit-due"
            label="تاريخ الاستحقاق"
            value={form.dueDate}
            onChange={(dueDate) => update({ dueDate })}
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
