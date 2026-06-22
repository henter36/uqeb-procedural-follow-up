import type { ReactNode } from 'react';

type TransactionActionPanelProps = Readonly<{
  title: string;
  open: boolean;
  onClose: () => void;
  children: ReactNode;
}>;

export default function TransactionActionPanel({
  title,
  open,
  onClose,
  children,
}: TransactionActionPanelProps) {
  if (!open) return null;

  return (
    <section className="workspace-action-panel" aria-label={title}>
      <div className="workspace-action-panel-header">
        <h3>{title}</h3>
        <button type="button" className="btn btn-ghost btn-sm" onClick={onClose} aria-label="إغلاق النموذج">
          إغلاق
        </button>
      </div>
      <div className="workspace-action-panel-body">
        {children}
      </div>
    </section>
  );
}
