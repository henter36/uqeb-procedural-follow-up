import type { ReactNode } from 'react';

type EmptyStateProps = Readonly<{
  title?: string;
  description?: string;
  action?: ReactNode;
  icon?: string;
}>;

export default function EmptyState({
  title = 'لا توجد بيانات',
  description,
  action,
  icon = '📋',
}: EmptyStateProps) {
  return (
    <div className="empty-state">
      <div className="empty-state-icon" aria-hidden="true">{icon}</div>
      <div className="empty-state-title">{title}</div>
      {description && <div className="empty-state-desc">{description}</div>}
      {action}
    </div>
  );
}
