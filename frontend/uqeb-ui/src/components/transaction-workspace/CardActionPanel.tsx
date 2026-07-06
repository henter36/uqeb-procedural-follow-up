import type { ReactNode, Ref } from 'react';

type CardActionPanelProps = Readonly<{
  title: string;
  onClose: () => void;
  children: ReactNode;
  testId?: string;
  panelRef?: Ref<HTMLElement>;
  prominent?: boolean;
}>;

export default function CardActionPanel({
  title,
  onClose,
  children,
  testId,
  panelRef,
  prominent = false,
}: CardActionPanelProps) {
  return (
    <section
      ref={panelRef}
      className={`card-action-panel${prominent ? ' card-action-panel--prominent' : ''}`}
      aria-label={title}
      data-testid={testId}
    >
      <div className="card-action-panel-header">
        <h4>{title}</h4>
        <button type="button" className="btn btn-ghost btn-sm" onClick={onClose} aria-label="إغلاق النموذج">
          إغلاق
        </button>
      </div>
      <div className="card-action-panel-body">
        {children}
      </div>
    </section>
  );
}
