import type { ReactNode } from 'react';

type ErrorStateProps = Readonly<{
  title?: string;
  description?: string;
  action?: ReactNode;
}>;

export default function ErrorState({
  title = 'حدث خطأ',
  description,
  action,
}: ErrorStateProps) {
  return (
    <div className="error-state" role="alert">
      <div className="error-state-icon" aria-hidden="true">⚠️</div>
      <div className="error-state-title">{title}</div>
      {description && <div className="error-state-desc">{description}</div>}
      {action}
    </div>
  );
}
