import type { ReactNode } from 'react';

type TransactionActionPanelProps = Readonly<{
  title: string;
  open: boolean;
  dirty?: boolean;
  onClose: () => void;
  children: ReactNode;
}>;

export default function TransactionActionPanel({
  title,
  open,
  dirty = false,
  onClose,
  children,
}: TransactionActionPanelProps) {
  if (!open) return null;

  const handleClose = () => {
    if (dirty && !globalThis.confirm('يوجد بيانات غير محفوظة. هل تريد إغلاق النموذج؟')) {
      return;
    }
    onClose();
  };

  return (
    <section className="workspace-action-panel" aria-label={title}>
      <div className="workspace-action-panel-header">
        <h3>{title}</h3>
        <button type="button" className="btn btn-ghost btn-sm" onClick={handleClose} aria-label="إغلاق النموذج">
          إغلاق
        </button>
      </div>
      <div className="workspace-action-panel-body">
        {children}
      </div>
    </section>
  );
}
