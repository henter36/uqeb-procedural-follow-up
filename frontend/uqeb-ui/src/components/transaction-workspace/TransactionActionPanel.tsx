import type { ReactNode, Ref } from 'react';

type TransactionActionPanelProps = Readonly<{
  title: string;
  open: boolean;
  onClose: () => void;
  children: ReactNode;
  panelRef?: Ref<HTMLElement>;
  prominent?: boolean;
}>;

export default function TransactionActionPanel({
  title,
  open,
  onClose,
  children,
  panelRef,
  prominent = false,
}: TransactionActionPanelProps) {
  if (!open) return null;

  return (
    <section
      ref={panelRef}
      className={`workspace-action-panel${prominent ? ' workspace-action-panel--prominent' : ''}`}
      aria-label={title}
    >
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
