import type { ReactNode } from 'react';

type PageHeaderProps = Readonly<{
  title: string;
  actions?: ReactNode;
  titleDescribedBy?: string;
}>;

export default function PageHeader({ title, actions, titleDescribedBy }: PageHeaderProps) {
  return (
    <div className="page-header">
      <div>
        <h2 className="page-title" aria-describedby={titleDescribedBy}>{title}</h2>
      </div>
      {actions && <div className="page-header-actions">{actions}</div>}
    </div>
  );
}
