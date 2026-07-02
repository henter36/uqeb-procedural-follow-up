import { useEffect, useRef, useState } from 'react';
import { departmentResponsesApi } from '../../api/services';
import type { DepartmentResponseDto, DepartmentTransactionResponseItemDto } from '../../api/types';
import { getApiErrorMessage } from '../../utils/apiHelpers';
import { Alert } from '../ui';

const STATUS_LABELS: Record<string, string> = {
  Draft: 'مسودة',
  SubmittedForReview: 'بانتظار المراجعة',
  ReturnedForCorrection: 'معادة للتصحيح',
  Approved: 'معتمدة',
  Rejected: 'مرفوضة',
};

type DepartmentResponseInlinePanelProps = Readonly<{
  transactionId: number;
  initialItem?: DepartmentTransactionResponseItemDto | null;
  onDirtyChange: (dirty: boolean) => void;
  onMessage: (message: string) => void;
  onCancel: () => void;
  onChanged: () => void | Promise<void>;
}>;

function isEditableStatus(status?: string): boolean {
  return !status || status === 'Draft' || status === 'ReturnedForCorrection';
}

function getTitle(status?: string): string {
  return status === 'Draft' || status === 'ReturnedForCorrection'
    ? 'استكمال إفادة الإدارة'
    : 'تسجيل إفادة الإدارة';
}

export default function DepartmentResponseInlinePanel({
  transactionId,
  initialItem,
  onDirtyChange,
  onMessage,
  onCancel,
  onChanged,
}: DepartmentResponseInlinePanelProps) {
  const responseTextRef = useRef<HTMLTextAreaElement>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const [item, setItem] = useState<DepartmentTransactionResponseItemDto | null | undefined>(initialItem);
  const [detail, setDetail] = useState<DepartmentResponseDto | null>(null);
  const [responseText, setResponseText] = useState('');
  const [loading, setLoading] = useState(Boolean(initialItem?.departmentResponseId));
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  const status = detail?.status ?? item?.departmentResponseStatus;
  const editable = isEditableStatus(status) && (item?.canCreateResponse || item?.canEditResponse || !item?.departmentResponseId);

  useEffect(() => {
    const responseId = initialItem?.departmentResponseId;
    if (!responseId) return;

    let active = true;
    departmentResponsesApi.getById(responseId)
      .then((res) => {
        if (!active) return;
        setDetail(res.data);
        setResponseText(res.data.responseText);
      })
      .catch((err: unknown) => {
        if (active) setError(getApiErrorMessage(err));
      })
      .finally(() => {
        if (active) setLoading(false);
      });

    return () => { active = false; };
  }, [initialItem?.departmentResponseId]);

  useEffect(() => {
    responseTextRef.current?.focus();
  }, [loading]);

  useEffect(() => {
    onDirtyChange(editable && responseText.trim() !== (detail?.responseText ?? ''));
  }, [detail?.responseText, editable, onDirtyChange, responseText]);

  async function saveDraft(): Promise<DepartmentResponseDto> {
    if (!responseText.trim()) {
      throw new Error('نص الإفادة مطلوب');
    }

    if (detail) {
      const res = await departmentResponsesApi.update(detail.id, { responseText });
      return res.data;
    }

    const res = await departmentResponsesApi.create({ transactionId, responseText });
    return res.data;
  }

  async function handleSaveDraft() {
    setSaving(true);
    setError('');
    try {
      const saved = await saveDraft();
      setDetail(saved);
      setItem((current) => ({
        transactionId,
        internalTrackingNumber: saved.internalTrackingNumber,
        subject: saved.transactionSubject,
        priority: current?.priority ?? 'Normal',
        departmentId: saved.departmentId,
        departmentName: saved.departmentName,
        departmentResponseId: saved.id,
        departmentResponseStatus: saved.status,
        canCreateResponse: false,
        canEditResponse: true,
        canSubmitResponse: true,
      }));
      onDirtyChange(false);
      onMessage('تم حفظ مسودة الإفادة.');
      await onChanged();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : getApiErrorMessage(err));
    } finally {
      setSaving(false);
    }
  }

  async function handleSubmit() {
    setSaving(true);
    setError('');
    try {
      const saved = await saveDraft();
      const submitted = await departmentResponsesApi.submit(saved.id);
      setDetail(submitted.data);
      setResponseText(submitted.data.responseText);
      setItem((current) => current ? {
        ...current,
        departmentResponseId: submitted.data.id,
        departmentResponseStatus: submitted.data.status,
        canCreateResponse: false,
        canEditResponse: false,
        canSubmitResponse: false,
      } : current);
      onDirtyChange(false);
      onMessage('تم إرسال الإفادة. الحالة: بانتظار المراجعة.');
      await onChanged();
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : getApiErrorMessage(err));
    } finally {
      setSaving(false);
    }
  }

  async function handleUpload(file: File) {
    if (!detail) return;
    setError('');
    try {
      const res = await departmentResponsesApi.uploadAttachment(detail.id, file);
      setDetail({ ...detail, attachments: [...detail.attachments, res.data] });
      onMessage('تم رفع مرفق الإفادة.');
    } catch (err: unknown) {
      setError(getApiErrorMessage(err));
    }
  }

  if (loading) return <div className="workspace-form" role="status">جارٍ تحميل إفادة الإدارة...</div>;

  return (
    <div className="workspace-form department-response-form">
      <div className="department-response-form-heading">
        <h4>{getTitle(status)}</h4>
        {status && <span className="badge badge-blue">{STATUS_LABELS[status] ?? status}</span>}
      </div>

      {status === 'ReturnedForCorrection' && (
        <Alert variant="warning">
          أُعيدت الإفادة للتصحيح.
          {detail?.reviewNote && <> ملاحظة المراجع: {detail.reviewNote}</>}
        </Alert>
      )}
      {status === 'Rejected' && detail?.reviewNote && (
        <Alert variant="error">سبب الرفض: {detail.reviewNote}</Alert>
      )}
      {error && <Alert variant="error">{error}</Alert>}

      {editable ? (
        <>
          <div className="form-group full-width">
            <label htmlFor="department-response-text">نص الإفادة *</label>
            <textarea
              id="department-response-text"
              ref={responseTextRef}
              required
              rows={8}
              value={responseText}
              onChange={(e) => setResponseText(e.target.value)}
              placeholder="اكتب إفادة الإدارة هنا..."
            />
          </div>

          <section className="department-response-attachments" aria-label="مرفقات الإفادة">
            <div className="department-response-attachments-header">
              <h5>المرفقات</h5>
              <button
                type="button"
                className="btn btn-secondary btn-sm"
                disabled={!detail}
                onClick={() => fileInputRef.current?.click()}
              >
                رفع مرفق
              </button>
              <input
                ref={fileInputRef}
                type="file"
                className="visually-hidden"
                aria-label="رفع مرفق إفادة"
                onChange={(e) => e.target.files?.[0] && handleUpload(e.target.files[0])}
              />
            </div>
            {!detail && <p className="text-muted">احفظ المسودة أولًا قبل رفع المرفقات.</p>}
            {detail && detail.attachments.length === 0 && <p className="text-muted">لا توجد مرفقات.</p>}
            {detail && detail.attachments.length > 0 && (
              <ul className="department-response-attachment-list">
                {detail.attachments.map((attachment) => (
                  <li key={attachment.id}>{attachment.originalFileName}</li>
                ))}
              </ul>
            )}
          </section>

          <div className="form-actions">
            <button type="button" className="btn btn-secondary" disabled={saving} onClick={handleSaveDraft}>
              {saving ? 'جارٍ الحفظ...' : 'حفظ كمسودة'}
            </button>
            <button type="button" className="btn btn-primary" disabled={saving} onClick={handleSubmit}>
              {saving ? 'جارٍ الإرسال...' : 'إرسال الإفادة'}
            </button>
            <button type="button" className="btn btn-outline" onClick={onCancel}>إلغاء</button>
          </div>
        </>
      ) : (
        <div className="department-response-readonly">
          <p className="text-muted">حالة الإفادة: {STATUS_LABELS[status ?? ''] ?? status}</p>
          {detail?.responseText && <p>{detail.responseText}</p>}
        </div>
      )}
    </div>
  );
}
