import { useEffect, useState, type FormEvent } from 'react';
import type { Department } from '../../api/types';
import { transactionsApi } from '../../api/services';
import { buildCreateAssignmentPayload, getApiErrorMessage } from '../../utils/apiHelpers';
import { Alert } from '../ui';

type AssignmentFormPanelProps = Readonly<{
  transactionId: number;
  departments: Department[];
  existingDepartmentIds: number[];
  onDirtyChange: (dirty: boolean) => void;
  onSuccess: () => void;
  onCancel: () => void;
}>;

function addDaysIso(baseDate: string, days: number): string {
  const date = new Date(baseDate);
  date.setDate(date.getDate() + days);
  return date.toISOString().split('T')[0];
}

export default function AssignmentFormPanel({
  transactionId,
  departments,
  existingDepartmentIds,
  onDirtyChange,
  onSuccess,
  onCancel,
}: AssignmentFormPanelProps) {
  const [form, setForm] = useState({
    departmentId: '',
    assignedDate: new Date().toISOString().split('T')[0],
    requiredAction: '',
    replyDueDays: '' as string | number,
    dueDate: '',
  });
  const [error, setError] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);

  useEffect(() => {
    const dirty = Boolean(
      form.departmentId || form.requiredAction || form.replyDueDays || form.dueDate,
    );
    onDirtyChange(dirty);
  }, [form, onDirtyChange]);

  const expectedDueDate = form.replyDueDays
    ? addDaysIso(form.assignedDate, Number(form.replyDueDays))
    : form.dueDate;

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (isSubmitting) return;
    if (form.departmentId && existingDepartmentIds.includes(Number(form.departmentId))) {
      setError('سبق تحويل المعاملة إلى هذه الإدارة.');
      return;
    }
    setError('');
    setIsSubmitting(true);
    try {
      await transactionsApi.addAssignment(transactionId, buildCreateAssignmentPayload(form));
      onSuccess();
    } catch (err: unknown) {
      setError(getApiErrorMessage(err));
    } finally {
      setIsSubmitting(false);
    }
  };

  const update = (patch: Partial<typeof form>) => setForm((prev) => ({ ...prev, ...patch }));

  return (
    <form onSubmit={submit} className="workspace-form">
      {error && <Alert variant="error">{error}</Alert>}
      {existingDepartmentIds.length > 0 && (
        <p className="text-muted workspace-form-hint">
          إدارات سبق التحويل إليها: {departments.filter((d) => existingDepartmentIds.includes(d.id)).map((d) => d.name).join('، ')}
        </p>
      )}
      <div className="form-grid">
        <div className="form-group">
          <label htmlFor="assignment-dept">الإدارة *</label>
          <select
            id="assignment-dept"
            required
            value={form.departmentId}
            onChange={(e) => update({ departmentId: e.target.value })}
          >
            <option value="">اختر الإدارة</option>
            {departments.map((d) => (
              <option key={d.id} value={d.id} disabled={existingDepartmentIds.includes(d.id)}>
                {d.name}{existingDepartmentIds.includes(d.id) ? ' (محوّلة سابقًا)' : ''}
              </option>
            ))}
          </select>
        </div>
        <div className="form-group">
          <label htmlFor="assignment-date">تاريخ التحويل</label>
          <input
            id="assignment-date"
            type="date"
            required
            value={form.assignedDate}
            onChange={(e) => update({ assignedDate: e.target.value })}
          />
        </div>
        <div className="form-group full-width">
          <label htmlFor="assignment-action">الإجراء المطلوب</label>
          <input
            id="assignment-action"
            value={form.requiredAction}
            onChange={(e) => update({ requiredAction: e.target.value })}
          />
        </div>
        <div className="form-group">
          <label htmlFor="assignment-days">عدد أيام الرد</label>
          <input
            id="assignment-days"
            type="number"
            min="1"
            value={form.replyDueDays}
            onChange={(e) => update({ replyDueDays: e.target.value, dueDate: '' })}
          />
        </div>
        <div className="form-group">
          <label htmlFor="assignment-due">أو تاريخ استحقاق محدد</label>
          <input
            id="assignment-due"
            type="date"
            value={form.dueDate}
            onChange={(e) => update({ dueDate: e.target.value, replyDueDays: '' })}
          />
        </div>
        {expectedDueDate && (
          <div className="form-group full-width">
            <span className="text-muted">تاريخ الرد المتوقع: {expectedDueDate}</span>
          </div>
        )}
      </div>
      <div className="form-actions">
        <button type="submit" className="btn btn-primary" disabled={isSubmitting}>
          {isSubmitting ? 'جاري الحفظ...' : 'حفظ التحويل'}
        </button>
        <button type="button" className="btn btn-outline" onClick={onCancel}>إلغاء</button>
      </div>
    </form>
  );
}
