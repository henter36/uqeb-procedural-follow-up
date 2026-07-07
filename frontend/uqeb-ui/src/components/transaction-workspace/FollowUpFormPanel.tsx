import { useEffect, useRef, useState, type FormEvent } from 'react';
import type { FollowUp, FollowUpDepartmentOption } from '../../api/types';
import { transactionsApi } from '../../api/services';
import { buildCreateFollowUpPayload, getApiErrorMessage } from '../../utils/apiHelpers';
import { areSortedIdsEqual } from '../../utils/formDirty';
import { FUTURE_EVENT_DATE_MESSAGE, isFutureLocalDate } from '../../utils/localDate';
import MultiSelect from '../MultiSelect';
import HijriDateInput from '../HijriDateInput';
import { Alert, LoadingInline } from '../ui';
import { formatDaysSince } from '../../utils/responseTiming';
import { createInitialFollowUpForm, getDefaultDepartmentIds } from './followUpDepartments';

type FollowUpFormState = ReturnType<typeof createInitialFollowUpForm>;

type FollowUpFormPanelProps = Readonly<{
  transactionId: number;
  daysSinceLastFollowUp?: number | null;
  onDirtyChange: (dirty: boolean) => void;
  onSuccess: (followUp: FollowUp) => void;
  onCancel: () => void;
}>;

async function loadFollowUpDepartments(transactionId: number): Promise<FollowUpDepartmentOption[]> {
  const response = await transactionsApi.getFollowUpDepartments(transactionId);
  return response.data;
}

function isFollowUpFormDirty(current: FollowUpFormState, initial: FollowUpFormState): boolean {
  return current.followUpNumber !== initial.followUpNumber
    || current.notes !== initial.notes
    || current.followUpDate !== initial.followUpDate
    || !areSortedIdsEqual(current.departmentIds, initial.departmentIds);
}

export default function FollowUpFormPanel(props: FollowUpFormPanelProps) {
  return <FollowUpFormPanelBody key={props.transactionId} {...props} />;
}

function FollowUpFormPanelBody({
  transactionId,
  daysSinceLastFollowUp,
  onDirtyChange,
  onSuccess,
  onCancel,
}: FollowUpFormPanelProps) {
  const [options, setOptions] = useState<FollowUpDepartmentOption[]>([]);
  const [loading, setLoading] = useState(true);
  const [form, setForm] = useState<FollowUpFormState>(() => createInitialFollowUpForm([]));
  const [error, setError] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const initialFormRef = useRef<FollowUpFormState | null>(null);

  useEffect(() => {
    let active = true;

    const load = async () => {
      try {
        const departments = await loadFollowUpDepartments(transactionId);
        if (!active) return;
        const defaultIds = getDefaultDepartmentIds(departments);
        const initial = createInitialFollowUpForm(defaultIds);
        initialFormRef.current = initial;
        setOptions(departments);
        setForm(initial);
        onDirtyChange(false);
      } catch {
        if (active) setError('تعذر تحميل الإدارات المتاحة');
      } finally {
        if (active) setLoading(false);
      }
    };

    load().catch(() => {
      if (active) setError('تعذر تحميل الإدارات المتاحة');
    });
    return () => { active = false; };
  }, [transactionId, onDirtyChange]);

  useEffect(() => {
    if (!initialFormRef.current) {
      onDirtyChange(false);
      return;
    }
    onDirtyChange(isFollowUpFormDirty(form, initialFormRef.current));
  }, [form, onDirtyChange]);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (isSubmitting || loading || !initialFormRef.current) return;
    if (form.departmentIds.length === 0) {
      setError('يجب اختيار إدارة واحدة على الأقل لإرسال التعقيب.');
      return;
    }
    if (!form.followUpDate) {
      setError('تاريخ التعقيب مطلوب.');
      return;
    }
    if (isFutureLocalDate(form.followUpDate)) {
      setError(FUTURE_EVENT_DATE_MESSAGE);
      return;
    }
    setError('');
    setIsSubmitting(true);
    try {
      const res = await transactionsApi.addFollowUp(transactionId, buildCreateFollowUpPayload(form));
      onSuccess(res.data);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err));
    } finally {
      setIsSubmitting(false);
    }
  };

  if (loading) {
    return (
      <div className="workspace-form">
        <LoadingInline label="جاري تحميل الإدارات..." />
        <div className="form-actions">
          <button type="button" className="btn btn-outline" onClick={onCancel}>إلغاء</button>
        </div>
      </div>
    );
  }

  return (
    <form onSubmit={submit} className="workspace-form" noValidate>
      {error && <Alert variant="error">{error}</Alert>}
      <p className="text-muted workspace-form-hint">
        منذ آخر تعقيب: {formatDaysSince(daysSinceLastFollowUp)}
      </p>
      {options.length === 0 ? (
        <Alert variant="error">لا توجد إدارات مرتبطة بهذه المعاملة. أضف احالةًا قبل إضافة التعقيب.</Alert>
      ) : (
        <div className="form-grid">
          <div className="form-group">
            <label htmlFor="followup-number">رقم التعقيب</label>
            <input id="followup-number" value={form.followUpNumber} onChange={(e) => setForm({ ...form, followUpNumber: e.target.value })} />
          </div>
          <div className="form-group">
            <HijriDateInput
              id="followup-date"
              label="تاريخ التعقيب"
              required
              value={form.followUpDate}
              onChange={(followUpDate) => setForm({ ...form, followUpDate })}
              disallowFutureDate
            />
          </div>
          <div className="form-group full-width">
            <MultiSelect
              label="مرسل إلى (إدارات)"
              options={options.map((d) => ({ id: d.departmentId, name: d.departmentName }))}
              selected={form.departmentIds}
              onChange={(ids) => setForm({ ...form, departmentIds: ids })}
            />
          </div>
          <div className="form-group full-width">
            <label htmlFor="followup-notes">ملاحظات التعقيب</label>
            <textarea id="followup-notes" value={form.notes} onChange={(e) => setForm({ ...form, notes: e.target.value })} />
          </div>
        </div>
      )}
      <div className="form-actions">
        {options.length > 0 && (
          <button type="submit" className="btn btn-primary" disabled={isSubmitting || loading}>
            {isSubmitting ? 'جاري الحفظ...' : 'حفظ التعقيب'}
          </button>
        )}
        <button type="button" className="btn btn-outline" onClick={onCancel}>إلغاء</button>
      </div>
    </form>
  );
}
