import type { ReactNode } from 'react';

type PageHeaderProps = Readonly<{
  title: string;
  actions?: ReactNode;
}>;

export default function PageHeader({ title, actions }: PageHeaderProps) {
  return (
    <div className="page-header">
      <div>
        <h2 className="page-title">{title}</h2>
      </div>
      {actions && <div className="page-header-actions">{actions}</div>}
    </div>
  );
}
