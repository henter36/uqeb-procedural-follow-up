import { Link } from 'react-router-dom';

export type BreadcrumbItem = {
  label: string;
  path?: string;
};

type BreadcrumbsProps = Readonly<{
  items: BreadcrumbItem[];
}>;

export default function Breadcrumbs({ items }: BreadcrumbsProps) {
  if (items.length === 0) return null;

  return (
    <nav className="breadcrumbs" aria-label="مسار التنقل">
      {items.map((item, index) => {
        const isLast = index === items.length - 1;
        return (
          <span key={`${item.label}-${index}`} className="breadcrumb-item">
            {index > 0 && <span className="breadcrumb-separator" aria-hidden="true">/</span>}
            {item.path && !isLast ? (
              <Link to={item.path}>{item.label}</Link>
            ) : (
              <span className={isLast ? 'breadcrumb-current' : undefined} aria-current={isLast ? 'page' : undefined}>
                {item.label}
              </span>
            )}
          </span>
        );
      })}
    </nav>
  );
}
