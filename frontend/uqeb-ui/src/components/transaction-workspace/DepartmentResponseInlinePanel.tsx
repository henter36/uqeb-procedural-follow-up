import { useEffect, useRef, useState } from 'react';
import { departmentResponsesApi } from '../../api/services';
import type { DepartmentResponseDto, DepartmentTransactionResponseItemDto } from '../../api/types';
import { getApiErrorMessage } from '../../utils/apiHelpers';
import ScanAttachmentButton from '../../features/scanner/ScanAttachmentButton';
import { Alert } from '../ui';
import { departmentResponseStatusLabels } from './departmentResponseStatusLabels';

type DepartmentResponseInlinePanelProps = Readonly<{
  transactionId: number;
  initialItem?: DepartmentTransactionResponseItemDto | null;
  onDirtyChange: (dirty: boolean) => void;
  onMessage: (message: string) => void;
  onCancel: () => void;
  onChanged: (detail: DepartmentResponseDto) => void | Promise<void>;
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
  const [uploadingAttachment, setUploadingAttachment] = useState(false);
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
    if (!loading) responseTextRef.current?.focus();
  }, [loading]);

  useEffect(() => {
    onDirtyChange(editable && responseText.trim() !== (detail?.responseText ?? '').trim());
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
      await onChanged(saved);
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
      await onChanged(submitted.data);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : getApiErrorMessage(err));
    } finally {
      setSaving(false);
    }
  }

  async function handleUpload(file: File) {
    if (!detail || uploadingAttachment) return;
    setError('');
    setUploadingAttachment(true);
    try {
      const res = await departmentResponsesApi.uploadAttachment(detail.id, file);
      setDetail((current) => (current ? {
        ...current,
        attachments: [...current.attachments, res.data],
      } : current));
      onMessage('تم رفع مرفق الإفادة.');
    } catch (err: unknown) {
      setError(getApiErrorMessage(err));
    } finally {
      setUploadingAttachment(false);
      if (fileInputRef.current) fileInputRef.current.value = '';
    }
  }

  async function handleScannedFile(file: File) {
    if (!detail) return;
    const res = await departmentResponsesApi.uploadAttachment(detail.id, file);
    setDetail((current) => (current ? {
      ...current,
      attachments: [...current.attachments, res.data],
    } : current));
    onMessage('تم رفع مرفق الإفادة من الماسح الضوئي.');
  }

  if (loading) {
    return (
      <output className="workspace-form department-response-inline-panel" aria-live="polite">
        جارٍ تحميل إفادة الإدارة...
      </output>
    );
  }

  return (
    <div className="workspace-form department-response-inline-panel">
      <div className="department-response-inline-header">
        <h4>{getTitle(status)}</h4>
        {status && <span className="badge badge-blue">{departmentResponseStatusLabels[status] ?? status}</span>}
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
          <div className="department-response-inline-body">
            <div className="form-group full-width">
              <label htmlFor="department-response-text">نص الإفادة *</label>
              <textarea
                id="department-response-text"
                ref={responseTextRef}
                className="department-response-inline-textarea"
                required
                rows={5}
                value={responseText}
                onChange={(e) => setResponseText(e.target.value)}
                placeholder="اكتب إفادة الإدارة هنا..."
              />
            </div>

            <section className="department-response-inline-attachments" aria-label="مرفقات الإفادة">
              <div className="department-response-inline-attachments-header">
                <h5>مرفقات الإفادة</h5>
                <div className="department-response-inline-attachment-actions">
                  <button
                    type="button"
                    className="btn btn-secondary btn-sm"
                    disabled={!detail || uploadingAttachment}
                    onClick={() => fileInputRef.current?.click()}
                  >
                    {uploadingAttachment ? 'جارٍ الرفع...' : 'رفع ملف'}
                  </button>
                  {detail ? (
                    <ScanAttachmentButton
                      transactionId={transactionId}
                      onSaved={() => undefined}
                      onSaveScannedFile={handleScannedFile}
                    />
                  ) : (
                    <button type="button" className="btn btn-secondary btn-sm" disabled>
                      مسح ضوئي
                    </button>
                  )}
                  <input
                    ref={fileInputRef}
                    type="file"
                    className="visually-hidden"
                    aria-label="رفع مرفق إفادة"
                    onChange={(e) => e.target.files?.[0] && handleUpload(e.target.files[0])}
                  />
                </div>
              </div>
              {!detail && <p className="text-muted">احفظ المسودة أولًا لإضافة المرفقات.</p>}
              {detail && detail.attachments.length === 0 && <p className="text-muted">لا توجد مرفقات.</p>}
              {detail && detail.attachments.length > 0 && (
                <ul className="department-response-inline-attachment-list">
                  {detail.attachments.map((attachment) => (
                    <li key={attachment.id}>{attachment.originalFileName}</li>
                  ))}
                </ul>
              )}
            </section>
          </div>

          <div className="department-response-inline-footer">
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
        <div className="department-response-inline-readonly">
          <p className="text-muted">حالة الإفادة: {departmentResponseStatusLabels[status ?? ''] ?? status}</p>
          {detail?.responseText && <p>{detail.responseText}</p>}
        </div>
      )}
    </div>
  );
}
