type TableSkeletonProps = {
  rows?: number;
  cols?: number;
};

export function TableSkeleton({ rows = 5, cols = 6 }: TableSkeletonProps) {
  return (
    <div className="table-wrapper">
      <table className="data-table" aria-hidden="true">
        <tbody>
          {Array.from({ length: rows }).map((_, row) => (
            <tr key={row} className="skeleton-row">
              {Array.from({ length: cols }).map((__, col) => (
                <td key={col}>
                  <div className={`skeleton-bar ${col % 3 === 0 ? 'w-60' : col % 3 === 1 ? 'w-80' : 'w-40'}`} />
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

export function StatsSkeleton({ count = 4 }: { count?: number }) {
  return (
    <div className="stats-grid" aria-hidden="true">
      {Array.from({ length: count }).map((_, i) => (
        <div key={i} className="stat-card">
          <div className="skeleton-bar w-40 skeleton-stat-value" />
          <div className="skeleton-bar w-60" />
        </div>
      ))}
    </div>
  );
}

export function LoadingInline({ label = 'جاري التحميل...' }: { label?: string }) {
  return (
    <div className="loading-inline" role="status" aria-live="polite">
      <span className="spinner" aria-hidden="true" />
      <span>{label}</span>
    </div>
  );
}
