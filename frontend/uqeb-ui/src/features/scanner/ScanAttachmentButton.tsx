import { lazy, Suspense, useState } from 'react';

const ScannerPanel = lazy(() => import('./ScannerPanel'));

interface ScanAttachmentButtonProps {
  transactionId: number;
  onSaved: () => void;
  onSaveScannedFile?: (file: File) => Promise<void>;
}

export default function ScanAttachmentButton({
  transactionId,
  onSaved,
  onSaveScannedFile,
}: ScanAttachmentButtonProps) {
  const [open, setOpen] = useState(false);

  return (
    <>
      <button
        type="button"
        className="btn btn-sm btn-secondary"
        onClick={() => setOpen(true)}
      >
        مسح ضوئي
      </button>

      {open && (
        <Suspense fallback={<div className="modal-overlay"><div className="loading">جاري التحميل...</div></div>}>
          <ScannerPanel
            transactionId={transactionId}
            onClose={() => setOpen(false)}
            onSaved={onSaved}
            onSaveScannedFile={onSaveScannedFile}
          />
        </Suspense>
      )}
    </>
  );
}
