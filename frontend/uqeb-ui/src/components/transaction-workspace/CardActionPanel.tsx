import type { ReactNode } from 'react';

type CardActionPanelProps = Readonly<{
  title: string;
  onClose: () => void;
  children: ReactNode;
  testId?: string;
}>;

export default function CardActionPanel({
  title,
  onClose,
  children,
  testId,
}: CardActionPanelProps) {
  return (
    <section
      className="card-action-panel"
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
