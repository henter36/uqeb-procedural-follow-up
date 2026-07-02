import { lazy, Suspense, useState } from 'react';

const ScannerPanel = lazy(() => import('./ScannerPanel'));

type ScanAttachmentButtonProps = Readonly<{
  transactionId: number;
  onSaved: () => void;
  onSaveScannedFile?: (file: File) => Promise<void>;
  beforeOpen?: () => Promise<boolean>;
  disabled?: boolean;
}>;

export default function ScanAttachmentButton({
  transactionId,
  onSaved,
  onSaveScannedFile,
  beforeOpen,
  disabled = false,
}: ScanAttachmentButtonProps) {
  const [open, setOpen] = useState(false);
  const [preparing, setPreparing] = useState(false);

  async function handleOpen() {
    if (disabled || preparing) return;

    setPreparing(true);
    try {
      const canOpen = beforeOpen ? await beforeOpen() : true;
      if (canOpen) setOpen(true);
    } finally {
      setPreparing(false);
    }
  }

  return (
    <>
      <button
        type="button"
        className="btn btn-sm btn-secondary"
        disabled={disabled || preparing}
        onClick={handleOpen}
      >
        {preparing ? 'جارٍ التجهيز...' : 'مسح ضوئي'}
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
