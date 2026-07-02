import { useEffect, useRef, useState, type FormEvent } from 'react';
import type { Department } from '../../api/types';
import { transactionsApi } from '../../api/services';
import { buildCreateAssignmentPayload, getApiErrorMessage } from '../../utils/apiHelpers';
import { addDaysIso, todayLocalIso } from '../../utils/localDate';
import { Alert } from '../ui';
import HijriDateInput from '../HijriDateInput';

type AssignmentFormState = {
  departmentId: string;
  assignedDate: string;
  letterNumber: string;
  requiredAction: string;
  replyDueDays: string | number;
  dueDate: string;
};

function createInitialAssignmentForm(): AssignmentFormState {
  return {
    departmentId: '',
    assignedDate: todayLocalIso(),
    letterNumber: '',
    requiredAction: '',
    replyDueDays: '',
    dueDate: '',
  };
}

function isAssignmentFormDirty(current: AssignmentFormState, initial: AssignmentFormState): boolean {
  return current.departmentId !== initial.departmentId
    || current.assignedDate !== initial.assignedDate
    || current.letterNumber !== initial.letterNumber
    || current.requiredAction !== initial.requiredAction
    || current.replyDueDays !== initial.replyDueDays
    || current.dueDate !== initial.dueDate;
}

type AssignmentFormPanelProps = Readonly<{
  transactionId: number;
  departments: Department[];
  existingDepartmentIds: number[];
  onDirtyChange: (dirty: boolean) => void;
  onSuccess: () => void;
  onCancel: () => void;
}>;

export default function AssignmentFormPanel({
  transactionId,
  departments,
  existingDepartmentIds,
  onDirtyChange,
  onSuccess,
  onCancel,
}: AssignmentFormPanelProps) {
  const initialForm = createInitialAssignmentForm();
  const initialFormRef = useRef<AssignmentFormState>(initialForm);
  const [form, setForm] = useState<AssignmentFormState>(initialForm);
  const [error, setError] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);

  useEffect(() => {
    onDirtyChange(isAssignmentFormDirty(form, initialFormRef.current));
  }, [form, onDirtyChange]);

  const expectedDueDate = form.replyDueDays
    ? addDaysIso(form.assignedDate, Number(form.replyDueDays))
    : form.dueDate;

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (isSubmitting) return;
    if (form.departmentId && existingDepartmentIds.includes(Number(form.departmentId))) {
      setError('سبق احالة المعاملة إلى هذه الإدارة.');
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

  const update = (patch: Partial<AssignmentFormState>) => setForm((prev) => ({ ...prev, ...patch }));

  return (
    <form onSubmit={submit} className="workspace-form">
      {error && <Alert variant="error">{error}</Alert>}
      {existingDepartmentIds.length > 0 && (
        <p className="text-muted workspace-form-hint">
          إدارات سبق الاحالة إليها: {departments.filter((d) => existingDepartmentIds.includes(d.id)).map((d) => d.name).join('، ')}
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
          <HijriDateInput
            id="assignment-date"
            label="تاريخ الاحالة"
            required
            value={form.assignedDate}
            onChange={(assignedDate) => update({ assignedDate })}
          />
        </div>
        <div className="form-group">
          <label htmlFor="assignment-letter">رقم الخطاب</label>
          <input
            id="assignment-letter"
            value={form.letterNumber}
            onChange={(e) => update({ letterNumber: e.target.value })}
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
          <HijriDateInput
            id="assignment-due"
            label="أو تاريخ استحقاق محدد"
            value={form.dueDate}
            onChange={(dueDate) => update({ dueDate, replyDueDays: '' })}
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
          {isSubmitting ? 'جاري الحفظ...' : 'حفظ الاحالة'}
        </button>
        <button type="button" className="btn btn-outline" onClick={onCancel}>إلغاء</button>
      </div>
    </form>
  );
}
