import { useEffect, useState, type FormEvent } from 'react';
import type { FollowUpDepartmentOption } from '../../api/types';
import { transactionsApi } from '../../api/services';
import { buildCreateFollowUpPayload, getApiErrorMessage } from '../../utils/apiHelpers';
import MultiSelect from '../MultiSelect';
import { Alert, LoadingInline } from '../ui';
import { formatDaysSince } from '../../utils/responseTiming';

type FollowUpFormPanelProps = Readonly<{
  transactionId: number;
  daysSinceLastFollowUp?: number | null;
  onDirtyChange: (dirty: boolean) => void;
  onSuccess: () => void;
  onCancel: () => void;
}>;

export default function FollowUpFormPanel({
  transactionId,
  daysSinceLastFollowUp,
  onDirtyChange,
  onSuccess,
  onCancel,
}: FollowUpFormPanelProps) {
  const [options, setOptions] = useState<FollowUpDepartmentOption[]>([]);
  const [loading, setLoading] = useState(true);
  const [form, setForm] = useState({
    followUpDate: new Date().toISOString().split('T')[0],
    notes: '',
    followUpNumber: '',
    departmentIds: [] as number[],
  });
  const [error, setError] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);

  useEffect(() => {
    let active = true;
    transactionsApi.getFollowUpDepartments(transactionId)
      .then((r) => {
        if (!active) return;
        setOptions(r.data);
        setForm((f) => ({
          ...f,
          departmentIds: r.data.filter((d) => d.isDefaultSelected).map((d) => d.departmentId),
        }));
      })
      .catch(() => { if (active) setError('تعذر تحميل الإدارات المتاحة'); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [transactionId]);

  useEffect(() => {
    const dirty = Boolean(form.followUpNumber || form.notes || form.departmentIds.length);
    onDirtyChange(dirty);
  }, [form, onDirtyChange]);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (isSubmitting) return;
    if (form.departmentIds.length === 0) {
      setError('يجب اختيار إدارة واحدة على الأقل لإرسال التعقيب.');
      return;
    }
    setError('');
    setIsSubmitting(true);
    try {
      await transactionsApi.addFollowUp(transactionId, buildCreateFollowUpPayload(form));
      onSuccess();
    } catch (err: unknown) {
      setError(getApiErrorMessage(err));
    } finally {
      setIsSubmitting(false);
    }
  };

  if (loading) return <LoadingInline label="جاري تحميل الإدارات..." />;

  return (
    <form onSubmit={submit} className="workspace-form">
      {error && <Alert variant="error">{error}</Alert>}
      <p className="text-muted workspace-form-hint">
        منذ آخر تعقيب: {formatDaysSince(daysSinceLastFollowUp)}
      </p>
      {options.length === 0 ? (
        <Alert variant="error">لا توجد إدارات مرتبطة بهذه المعاملة. أضف تحويلًا قبل إضافة التعقيب.</Alert>
      ) : (
        <>
          <div className="form-grid">
            <div className="form-group">
              <label htmlFor="followup-number">رقم التعقيب</label>
              <input id="followup-number" value={form.followUpNumber} onChange={(e) => setForm({ ...form, followUpNumber: e.target.value })} />
            </div>
            <div className="form-group">
              <label htmlFor="followup-date">تاريخ التعقيب</label>
              <input id="followup-date" type="date" required value={form.followUpDate} onChange={(e) => setForm({ ...form, followUpDate: e.target.value })} />
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
          <div className="form-actions">
            <button type="submit" className="btn btn-primary" disabled={isSubmitting}>
              {isSubmitting ? 'جاري الحفظ...' : 'حفظ التعقيب'}
            </button>
            <button type="button" className="btn btn-outline" onClick={onCancel}>إلغاء</button>
          </div>
        </>
      )}
    </form>
  );
}
