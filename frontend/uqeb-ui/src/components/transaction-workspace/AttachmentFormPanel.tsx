import { useState, type ChangeEvent } from 'react';
import { transactionsApi } from '../../api/services';
import { getApiErrorMessage } from '../../utils/apiHelpers';
import ScanAttachmentButton from '../../features/scanner/ScanAttachmentButton';
import { Alert } from '../ui';

const ALLOWED_EXTENSIONS = ['.pdf', '.doc', '.docx', '.xls', '.xlsx', '.png', '.jpg', '.jpeg', '.gif', '.webp', '.txt'];
const MAX_BYTES = 20 * 1024 * 1024;

type AttachmentFormPanelProps = Readonly<{
  transactionId: number;
  onDirtyChange: (dirty: boolean) => void;
  onSuccess: () => void;
  onCancel: () => void;
}>;

function isAllowedFile(file: File): boolean {
  const lower = file.name.toLowerCase();
  return ALLOWED_EXTENSIONS.some((ext) => lower.endsWith(ext));
}

export default function AttachmentFormPanel({
  transactionId,
  onDirtyChange,
  onSuccess,
  onCancel,
}: AttachmentFormPanelProps) {
  const [error, setError] = useState('');
  const [uploading, setUploading] = useState(false);
  const [progressLabel, setProgressLabel] = useState('');

  const uploadFile = async (file: File) => {
    if (uploading) return;
    if (!isAllowedFile(file)) {
      setError(`نوع الملف غير مسموح. الأنواع المسموحة: ${ALLOWED_EXTENSIONS.join(', ')}`);
      onDirtyChange(false);
      return;
    }
    if (file.size > MAX_BYTES) {
      setError('حجم الملف يتجاوز الحد المسموح (20 ميجابايت).');
      onDirtyChange(false);
      return;
    }
    setError('');
    setUploading(true);
    setProgressLabel(`جاري رفع ${file.name}...`);
    try {
      await transactionsApi.uploadAttachment(transactionId, file);
      onSuccess();
    } catch (err: unknown) {
      setError(getApiErrorMessage(err));
    } finally {
      onDirtyChange(false);
      setUploading(false);
      setProgressLabel('');
    }
  };

  const handleFileChange = async (event: ChangeEvent<HTMLInputElement>) => {
    const file = event.target.files?.[0];
    event.target.value = '';
    if (file) {
      await uploadFile(file);
    }
  };

  return (
    <div className="workspace-form">
      {error && <Alert variant="error">{error}</Alert>}
      {progressLabel && (
        <output className="text-muted" aria-live="polite">{progressLabel}</output>
      )}
      <div className="workspace-attachment-actions">
        <label className="btn btn-primary">
          <span>اختيار ملف من الجهاز</span>
          <input
            type="file"
            hidden
            disabled={uploading}
            onChange={handleFileChange}
          />
        </label>
        <ScanAttachmentButton transactionId={transactionId} onSaved={onSuccess} />
      </div>
      <p className="text-muted workspace-form-hint">
        الحد الأقصى 20 ميجابايت. لا يُعتمد على الامتداد وحده كدليل على نوع الملف الآمن.
      </p>
      <div className="form-actions">
        <button type="button" className="btn btn-outline" onClick={onCancel} disabled={uploading}>إلغاء</button>
      </div>
    </div>
  );
}
