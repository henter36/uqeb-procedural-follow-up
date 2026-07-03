import { useEffect, useState, type FormEvent } from 'react';
import type { TransactionDetail } from '../../api/types';
import { transactionsApi } from '../../api/services';
import { getApiErrorMessage, getFieldErrors } from '../../utils/apiHelpers';
import { Alert } from '../ui';
import { formatHijri } from '../../utils/dateUtils';

const RECURRENCE_TYPE_OPTIONS = [
  { value: 'Monthly', label: 'شهري' },
  { value: 'Quarterly', label: 'ربع سنوي' },
  { value: 'SemiAnnual', label: 'نصف سنوي' },
  { value: 'Annual', label: 'سنوي' },
];

type FormState = {
  recurrenceType: string;
  nextTransactionCreationMethod: string;
};

type Props = Readonly<{
  transactionId: number;
  incomingDate: string;
  onDirtyChange: (dirty: boolean) => void;
  onCancel: () => void;
  onSuccess: (updated: TransactionDetail) => void;
}>;

function calculateRecurringPeriodEnd(startDate: string, recurrenceType: string): Date | null {
  if (!startDate) return null;
  const monthsByType: Record<string, number> = {
    Monthly: 1,
    Quarterly: 3,
    SemiAnnual: 6,
    Annual: 12,
  };
  const months = monthsByType[recurrenceType];
  if (!months) return null;

  const date = new Date(`${startDate.split('T')[0]}T00:00:00`);
  date.setMonth(date.getMonth() + months);
  return date;
}

export default function EnableRecurringFormPanel({ transactionId, incomingDate, onDirtyChange, onCancel, onSuccess }: Props) {
  const [form, setForm] = useState<FormState>({
    recurrenceType: 'Monthly',
    nextTransactionCreationMethod: 'Manual',
  });
  const [error, setError] = useState('');
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [saving, setSaving] = useState(false);

  const dirty = Boolean(
    form.recurrenceType !== 'Monthly' ||
    form.nextTransactionCreationMethod !== 'Manual',
  );

  useEffect(() => {
    onDirtyChange(dirty);
  }, [dirty, onDirtyChange]);

  const update = (patch: Partial<FormState>) => setForm((prev) => ({ ...prev, ...patch }));

  const fieldError = (name: string) => fieldErrors[name];
  const expectedPeriodEnd = calculateRecurringPeriodEnd(incomingDate, form.recurrenceType);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (saving) return;
    setSaving(true);
    setError('');
    setFieldErrors({});
    try {
      const res = await transactionsApi.enableRecurring(transactionId, {
        recurrenceType: form.recurrenceType,
        startDate: null,
        endDate: null,
        dueDaysAfterPeriodEnd: 0,
        nextTransactionCreationMethod: form.nextTransactionCreationMethod,
      });
      onDirtyChange(false);
      onSuccess(res.data);
    } catch (err: unknown) {
      const errs = getFieldErrors(err);
      if (Object.keys(errs).length > 0) setFieldErrors(errs);
      else setError(getApiErrorMessage(err));
    } finally {
      setSaving(false);
    }
  };

  return (
    <form onSubmit={submit} className="workspace-form">
      <Alert variant="info">
        تفعيل المتابعة الدورية ينشئ قالب التزام دوري جديد مرتبطًا بهذه المعاملة. تبقى هذه المعاملة كما هي، ويمكن
        توليد معاملة كل فترة لاحقًا من شاشة الالتزامات الدورية.
      </Alert>
      {error && <Alert variant="error">{error}</Alert>}
      <div className="form-grid">
        <div className="form-group">
          <label htmlFor="enable-recurring-type">نوع التكرار *</label>
          <select
            id="enable-recurring-type"
            value={form.recurrenceType}
            onChange={(e) => update({ recurrenceType: e.target.value })}
          >
            {RECURRENCE_TYPE_OPTIONS.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
          </select>
          {fieldError('RecurrenceType') && <span className="field-error">{fieldError('RecurrenceType')}</span>}
        </div>
        <div className="form-group">
          <span className="form-label">نهاية الفترة الأولى المتوقعة</span>
          <p className="text-muted">{expectedPeriodEnd ? formatHijri(expectedPeriodEnd) : '—'}</p>
        </div>
        <div className="form-group">
          <span className="form-label">طريقة إنشاء المعاملة التالية</span>
          <div className="radio-group">
            <label className="radio-label">
              <input
                type="radio"
                name="nextTransactionCreationMethod"
                checked={form.nextTransactionCreationMethod === 'Manual'}
                onChange={() => update({ nextTransactionCreationMethod: 'Manual' })}
              />
              <span>يدويًا من شاشة الالتزامات الدورية</span>
            </label>
            <label className="radio-label">
              <input
                type="radio"
                name="nextTransactionCreationMethod"
                checked={form.nextTransactionCreationMethod === 'AutomaticOnClose'}
                onChange={() => update({ nextTransactionCreationMethod: 'AutomaticOnClose' })}
              />
              <span>تلقائيًا عند إغلاق المعاملة الحالية</span>
            </label>
          </div>
        </div>
      </div>
      <div className="form-actions">
        <button type="submit" className="btn btn-primary" disabled={saving}>
          {saving ? 'جاري الحفظ...' : 'تفعيل المتابعة الدورية'}
        </button>
        <button type="button" className="btn btn-outline" onClick={onCancel} disabled={saving}>إلغاء</button>
      </div>
    </form>
  );
}
