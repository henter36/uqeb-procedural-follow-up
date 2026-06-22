type TableSkeletonProps = Readonly<{
  rows?: number;
  cols?: number;
}>;

function skeletonBarWidthClass(col: number): string {
  const mod = col % 3;
  if (mod === 0) return 'w-60';
  if (mod === 1) return 'w-80';
  return 'w-40';
}

export function TableSkeleton({ rows = 5, cols = 6 }: TableSkeletonProps) {
  return (
    <div className="table-wrapper">
      <table className="data-table" aria-hidden="true">
        <tbody>
          {Array.from({ length: rows }, (_, row) => (
            <tr key={`skeleton-row-${row}`} className="skeleton-row">
              {Array.from({ length: cols }, (__, col) => (
                <td key={`skeleton-cell-${row}-${col}`}>
                  <div className={`skeleton-bar ${skeletonBarWidthClass(col)}`} />
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export function CardSkeleton() {
  return (
    <div className="card" aria-hidden="true">
      <div className="skeleton-bar w-40 skeleton-mb" />
      <div className="skeleton-bar w-80 skeleton-mb-sm" />
      <div className="skeleton-bar w-60" />
    </div>
  );
}

type StatsSkeletonProps = Readonly<{
  count?: number;
}>;

export function StatsSkeleton({ count = 4 }: StatsSkeletonProps) {
  return (
    <div className="stats-grid" aria-hidden="true">
      {Array.from({ length: count }, (_, index) => (
        <div key={`stat-skeleton-${index}`} className="stat-card">
          <div className="skeleton-bar w-40 skeleton-stat-value" />
          <div className="skeleton-bar w-60" />
        </div>
      ))}
    </div>
  );
}

type LoadingInlineProps = Readonly<{
  label?: string;
}>;

export function LoadingInline({ label = 'جاري التحميل...' }: LoadingInlineProps) {
  return (
    <div className="loading-inline" role="status" aria-live="polite">
      <span className="spinner" aria-hidden="true" />
      <span>{label}</span>
    </div>
  );
}
