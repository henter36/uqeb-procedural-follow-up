import { useEffect, useState, type FormEvent } from 'react';
import type { DepartmentResponseDto } from '../../api/types';
import { departmentResponsesApi } from '../../api/services';
import { getApiErrorMessage } from '../../utils/apiHelpers';
import { FUTURE_EVENT_DATE_MESSAGE, isFutureLocalDate } from '../../utils/localDate';
import { Alert, LoadingInline } from '../ui';
import HijriDateInput from '../HijriDateInput';
import { AdminEditAuditHint, AdminEditFormActions, AdminEditReasonField } from './AdminEditFormShared';

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
    onDirtyChange(dirty);
  }, [dirty, onDirtyChange]);

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
    if (isFutureLocalDate(form.submittedAt)) {
      setError(FUTURE_EVENT_DATE_MESSAGE);
      return;
    }
    setSaving(true);
    setError('');
    try {
      const payload: Record<string, unknown> = {
        reason: form.reason.trim(),
        responseText: form.responseText.trim() || null,
        submittedAt: form.submittedAt || null,
      };
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
      <AdminEditAuditHint />
      {error && <Alert variant="error">{error}</Alert>}
      <div className="form-grid">
        <div className="form-group full-width">
          <label htmlFor="admin-resp-text">ملخص الإفادة</label>
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
            disallowFutureDate
          />
          <small className="text-muted">هذا التاريخ يمثل تاريخ الإفادة/إنجاز رد الإدارة، ويستخدم في احتساب أيام إنجاز الإدارة.</small>
        </div>
        <AdminEditReasonField
          id="admin-resp-reason"
          value={form.reason}
          onChange={(reason) => update({ reason })}
        />
      </div>
      <AdminEditFormActions saving={saving} dirty={dirty} reason={form.reason} onCancel={onCancel} />
    </form>
  );
}
