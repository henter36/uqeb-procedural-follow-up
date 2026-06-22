import { useState } from 'react';
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
      return;
    }
    if (file.size > MAX_BYTES) {
      setError('حجم الملف يتجاوز الحد المسموح (20 ميجابايت).');
      return;
    }
    setError('');
    setUploading(true);
    setProgressLabel(`جاري رفع ${file.name}...`);
    onDirtyChange(true);
    try {
      await transactionsApi.uploadAttachment(transactionId, file);
      onDirtyChange(false);
      onSuccess();
    } catch (err: unknown) {
      setError(getApiErrorMessage(err));
    } finally {
      setUploading(false);
      setProgressLabel('');
    }
  };

  return (
    <div className="workspace-form">
      {error && <Alert variant="error">{error}</Alert>}
      {progressLabel && <p className="text-muted" role="status">{progressLabel}</p>}
      <div className="workspace-attachment-actions">
        <label className="btn btn-primary">
          اختيار ملف من الجهاز
          <input
            type="file"
            hidden
            disabled={uploading}
            onChange={(e) => {
              const file = e.target.files?.[0];
              if (file) void uploadFile(file);
              e.target.value = '';
            }}
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
