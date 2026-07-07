import { lazy, Suspense, useEffect, useRef, useState, type FormEvent } from 'react';
import { transactionsApi } from '../../api/services';
import { buildCompleteResponsePayload, getApiErrorMessage } from '../../utils/apiHelpers';
import { FUTURE_EVENT_DATE_MESSAGE, isFutureLocalDate } from '../../utils/localDate';
import { Alert } from '../ui';
import HijriDateInput from '../HijriDateInput';

const ScannerPanel = lazy(() => import('../../features/scanner/ScannerPanel'));

export type CompleteResponseSuccessResult = Readonly<{
  attachmentWarning?: string;
}>;

type CompleteResponseFormPanelProps = Readonly<{
  transactionId: number;
  responseType: string;
  onDirtyChange: (dirty: boolean) => void;
  onSuccess: (result?: CompleteResponseSuccessResult) => void;
  onCancel: () => void;
}>;

const ATTACHMENT_PARTIAL_WARNING = 'تم تسجيل الإفادة، لكن تعذر رفع المرفق. يمكنك رفعه من قسم المرفقات.';
type AttachmentUploadResult = 'success' | 'partial-warning' | 'none';

export default function CompleteResponseFormPanel({
  transactionId,
  responseType,
  onDirtyChange,
  onSuccess,
  onCancel,
}: CompleteResponseFormPanelProps) {
  const requiresOutgoing = responseType === 'External' || responseType === 'Both';
  const [form, setForm] = useState({
    responseDate: '',
    responseSummary: '',
    outgoingNumber: '',
    outgoingDate: '',
  });
  const [attachment, setAttachment] = useState<File | null>(null);
  const [scannerOpen, setScannerOpen] = useState(false);
  const [error, setError] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [responseSaved, setResponseSaved] = useState(false);
  const responseSavedRef = useRef(false);
  const fileInputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    const dirty = Boolean(
      form.responseDate
      || form.responseSummary.trim()
      || form.outgoingNumber.trim()
      || form.outgoingDate
      || attachment,
    );
    onDirtyChange(dirty && !responseSaved);
  }, [form, attachment, onDirtyChange, responseSaved]);

  const validateBeforeSubmit = (): string | null => {
    if (isSubmitting) return '';
    if (responseSaved) return attachment ? null : '';
    if (!form.responseDate) return 'تاريخ الإفادة مطلوب.';
    if (!form.responseSummary.trim()) return 'ملخص الإفادة مطلوب';
    if (isFutureLocalDate(form.responseDate) || isFutureLocalDate(form.outgoingDate)) {
      return FUTURE_EVENT_DATE_MESSAGE;
    }
    if (requiresOutgoing && !form.outgoingDate) return 'تاريخ الصادر مطلوب.';
    if (requiresOutgoing && !form.outgoingNumber.trim()) return 'رقم الصادر مطلوب.';
    return null;
  };

  const saveResponseIfNeeded = async (): Promise<void> => {
    if (responseSavedRef.current) return;

    await transactionsApi.completeResponse(
      transactionId,
      buildCompleteResponsePayload({ ...form, requiresOutgoing }),
    );
    responseSavedRef.current = true;
    setResponseSaved(true);
    onDirtyChange(false);
  };

  const uploadAttachmentIfPresent = async (): Promise<AttachmentUploadResult> => {
    if (!attachment) return 'none';

    try {
      await transactionsApi.uploadAttachment(transactionId, attachment, 'Response');
      return 'success';
    } catch {
      setAttachment(null);
      return 'partial-warning';
    }
  };

  const submit = async (e: FormEvent) => {
    e.preventDefault();

    const validationError = validateBeforeSubmit();
    if (validationError !== null) {
      if (validationError) setError(validationError);
      return;
    }

    setError('');
    setIsSubmitting(true);
    try {
      await saveResponseIfNeeded();
      const attachmentResult = await uploadAttachmentIfPresent();
      if (attachmentResult === 'partial-warning') {
        onSuccess({ attachmentWarning: ATTACHMENT_PARTIAL_WARNING });
      } else {
        onSuccess();
      }
    } catch (err: unknown) {
      setError(getApiErrorMessage(err));
    } finally {
      setIsSubmitting(false);
    }
  };

  const attachmentLabel = attachment ? attachment.name : 'لا يوجد';

  return (
    <form onSubmit={submit} className="workspace-form complete-response-compact" noValidate>
      {error && <Alert variant="error">{error}</Alert>}
      <p className="text-muted workspace-form-hint">
        تسجيل الإفادة يُغلق إحالة الإدارة. التاريخ المدخل أدناه هو تاريخ الإفادة الفعلي وسيُحفظ كما هو.
      </p>

      <div className="complete-response-compact-body">
        <div className="form-group">
          <HijriDateInput
            id="response-date"
            label="تاريخ الإفادة"
            required
            value={form.responseDate}
            disabled={responseSaved}
            onChange={(responseDate) => setForm({ ...form, responseDate })}
            disallowFutureDate
          />
        </div>

        <div className="form-group full-width">
          <label htmlFor="response-summary">ملخص الإفادة *</label>
          <textarea
            id="response-summary"
            required
            rows={4}
            value={form.responseSummary}
            disabled={responseSaved}
            onChange={(e) => setForm({ ...form, responseSummary: e.target.value })}
          />
        </div>

        {requiresOutgoing && (
          <>
            <div className="form-group">
              <label htmlFor="outgoing-number">رقم الصادر *</label>
              <input
                id="outgoing-number"
                required
                value={form.outgoingNumber}
                disabled={responseSaved}
                onChange={(e) => setForm({ ...form, outgoingNumber: e.target.value })}
              />
            </div>
            <div className="form-group">
              <HijriDateInput
                id="outgoing-date"
                label="تاريخ الصادر"
                required
                value={form.outgoingDate}
                disabled={responseSaved}
                onChange={(outgoingDate) => setForm({ ...form, outgoingDate })}
                disallowFutureDate
              />
            </div>
          </>
        )}

        <fieldset className="complete-response-attachment-toolbar">
          <legend className="visually-hidden">مرفق الإفادة</legend>
          <span className="complete-response-attachment-label">مرفق: {attachmentLabel}</span>
          <div className="complete-response-attachment-actions">
            <button
              type="button"
              className="btn btn-secondary btn-sm"
              disabled={isSubmitting}
              onClick={() => fileInputRef.current?.click()}
            >
              رفع ملف
            </button>
            <button
              type="button"
              className="btn btn-secondary btn-sm"
              disabled={isSubmitting}
              onClick={() => setScannerOpen(true)}
            >
              مسح ضوئي
            </button>
            <input
              ref={fileInputRef}
              type="file"
              className="visually-hidden"
              aria-label="مرفق (اختياري)"
              onChange={(e) => setAttachment(e.target.files?.[0] ?? null)}
            />
          </div>
        </fieldset>
      </div>

      <div className="complete-response-compact-footer">
        <button type="button" className="btn btn-outline btn-sm" onClick={onCancel}>إلغاء</button>
        <button type="submit" className="btn btn-primary btn-sm" disabled={isSubmitting}>
          {isSubmitting ? 'جاري الحفظ...' : 'إرسال الإفادة'}
        </button>
      </div>

      {scannerOpen && (
        <Suspense>
          <ScannerPanel
            transactionId={transactionId}
            onClose={() => setScannerOpen(false)}
            onSaved={() => setScannerOpen(false)}
            onSaveScannedFile={async (file) => {
              setAttachment(file);
            }}
          />
        </Suspense>
      )}
    </form>
  );
}
