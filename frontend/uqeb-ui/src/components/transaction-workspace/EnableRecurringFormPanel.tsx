import { useEffect, useState, type FormEvent } from 'react';
import type { TransactionDetail } from '../../api/types';
import { transactionsApi } from '../../api/services';
import { getApiErrorMessage, getFieldErrors } from '../../utils/apiHelpers';
import { Alert } from '../ui';
import HijriDateInput from '../HijriDateInput';

const RECURRENCE_TYPE_OPTIONS = [
  { value: 'Monthly', label: 'شهري' },
  { value: 'Quarterly', label: 'ربع سنوي' },
  { value: 'SemiAnnual', label: 'نصف سنوي' },
  { value: 'Annual', label: 'سنوي' },
];

type FormState = {
  recurrenceType: string;
  endDate: string;
  nextTransactionCreationMethod: string;
};

type Props = Readonly<{
  transactionId: number;
  onDirtyChange: (dirty: boolean) => void;
  onCancel: () => void;
  onSuccess: (updated: TransactionDetail) => void;
}>;

export default function EnableRecurringFormPanel({ transactionId, onDirtyChange, onCancel, onSuccess }: Props) {
  const [form, setForm] = useState<FormState>({
    recurrenceType: 'Monthly',
    endDate: '',
    nextTransactionCreationMethod: 'Manual',
  });
  const [error, setError] = useState('');
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [saving, setSaving] = useState(false);

  const dirty = Boolean(
    form.endDate ||
    form.recurrenceType !== 'Monthly' ||
    form.nextTransactionCreationMethod !== 'Manual',
  );

  useEffect(() => {
    onDirtyChange(dirty);
  }, [dirty, onDirtyChange]);

  const update = (patch: Partial<FormState>) => setForm((prev) => ({ ...prev, ...patch }));

  const fieldError = (name: string) => fieldErrors[name];

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
        endDate: form.endDate || null,
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
          <HijriDateInput
            id="enable-recurring-end"
            label="نهاية الالتزام"
            value={form.endDate}
            onChange={(endDate) => update({ endDate })}
            invalid={Boolean(fieldError('EndDate'))}
          />
          {fieldError('EndDate') && <span className="field-error">{fieldError('EndDate')}</span>}
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
