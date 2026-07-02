import { useEffect, useState, type FormEvent } from 'react';
import type { DepartmentResponseDto } from '../../api/types';
import { departmentResponsesApi } from '../../api/services';
import { getApiErrorMessage } from '../../utils/apiHelpers';
import { Alert, LoadingInline } from '../ui';
import HijriDateInput from '../HijriDateInput';

type FormState = {
  responseText: string;
  submittedAt: string;
  reason: string;
};

function fromResponse(r?: DepartmentResponseDto): FormState {
  return {
    responseText: r?.responseText ?? '',
    submittedAt: r?.submittedAt?.slice(0, 10) ?? '',
    reason: '',
  };
}

type Props = Readonly<{
  responseId: number;
  initialResponse?: DepartmentResponseDto;
  onDirtyChange: (dirty: boolean) => void;
  onCancel: () => void;
  onSuccess: (updated: DepartmentResponseDto) => void;
}>;

export default function AdminEditResponseFormPanel({
  responseId,
  initialResponse,
  onDirtyChange,
  onCancel,
  onSuccess,
}: Props) {
  const [response, setResponse] = useState<DepartmentResponseDto | undefined>(initialResponse);
  const [loading, setLoading] = useState(!initialResponse);
  const [loadError, setLoadError] = useState('');
  const [initialForm, setInitialForm] = useState<FormState>(() => fromResponse(initialResponse));
  const [form, setForm] = useState<FormState>(initialForm);
  const [error, setError] = useState('');
  const [saving, setSaving] = useState(false);

  const dirty = form.responseText !== initialForm.responseText
    || form.submittedAt !== initialForm.submittedAt;

  useEffect(() => {
    onDirtyChange(dirty && form.reason.trim().length > 0);
  }, [dirty, form.reason, onDirtyChange]);

  useEffect(() => {
    let cancelled = false;

    if (initialResponse) return undefined;

    departmentResponsesApi.getById(responseId)
      .then((res) => {
        if (cancelled) return;
        const nextForm = fromResponse(res.data);
        setResponse(res.data);
        setInitialForm(nextForm);
        setForm(nextForm);
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        setLoadError(getApiErrorMessage(err));
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });

    return () => {
      cancelled = true;
    };
  }, [initialResponse, responseId]);

  const update = (patch: Partial<FormState>) => setForm((prev) => ({ ...prev, ...patch }));

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (saving) return;
    if (!form.reason.trim()) {
      setError('سبب التعديل مطلوب.');
      return;
    }
    setSaving(true);
    setError('');
    try {
      const payload: Record<string, unknown> = { reason: form.reason.trim() };
      if (form.responseText.trim()) payload.responseText = form.responseText.trim();
      if (form.submittedAt) payload.submittedAt = form.submittedAt;
      const res = await departmentResponsesApi.adminEdit(responseId, payload);
      onSuccess(res.data);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err));
    } finally {
      setSaving(false);
    }
  };

  if (loading) {
    return <LoadingInline label="جاري تحميل بيانات الرد..." />;
  }

  if (loadError) {
    return <Alert variant="error">{loadError}</Alert>;
  }

  if (!response) {
    return <Alert variant="error">تعذر تحميل بيانات الرد.</Alert>;
  }

  return (
    <form onSubmit={submit} className="workspace-form">
      <p className="text-muted workspace-form-hint">
        هذا النموذج للتصحيح الإداري فقط. كل تعديل يُسجَّل في سجل التدقيق مع السبب.
      </p>
      {error && <Alert variant="error">{error}</Alert>}
      <div className="form-grid">
        <div className="form-group full-width">
          <label htmlFor="admin-resp-text">نص الرد</label>
          <textarea
            id="admin-resp-text"
            rows={4}
            value={form.responseText}
            onChange={(e) => update({ responseText: e.target.value })}
          />
          <small className="text-muted">محتوى إفادة الإدارة المقدَّم.</small>
        </div>
        <div className="form-group">
          <HijriDateInput
            id="admin-resp-submitted-at"
            label="تاريخ إنجاز الإدارة"
            value={form.submittedAt}
            onChange={(submittedAt) => update({ submittedAt })}
          />
          <small className="text-muted">يُستخدم لحساب أيام إنجاز الإدارة.</small>
        </div>
        <div className="form-group full-width">
          <label htmlFor="admin-resp-reason">سبب التعديل *</label>
          <input
            id="admin-resp-reason"
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
