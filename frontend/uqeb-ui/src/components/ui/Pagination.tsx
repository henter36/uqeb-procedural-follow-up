type PaginationProps = Readonly<{
  page: number;
  pageSize: number;
  total: number;
  itemCount: number;
  onPageChange: (page: number) => void;
  onPageSizeChange?: (size: number) => void;
  pageSizeOptions?: number[];
}>;

export default function Pagination({
  page,
  pageSize,
  total,
  itemCount,
  onPageChange,
  onPageSizeChange,
  pageSizeOptions = [10, 20, 25, 50],
}: PaginationProps) {
  const totalPages = Math.max(1, Math.ceil(total / pageSize));
  const safePage = Math.min(Math.max(1, page), totalPages);
  const from = total === 0 ? 0 : (safePage - 1) * pageSize + 1;
  const to = total === 0 ? 0 : Math.min(safePage * pageSize, total);

  return (
    <nav className="pagination" aria-label="التصفح">
      <span className="pagination-info">
        عرض {from}–{to} من {total}
      </span>

      {onPageSizeChange && (
        <label className="pagination-size">
          <span className="text-muted pagination-size-label">لكل صفحة:</span>
          <select
            value={pageSize}
            onChange={(e) => onPageSizeChange(Number(e.target.value))}
            aria-label="عدد السجلات لكل صفحة"
          >
            {pageSizeOptions.map((size) => (
              <option key={size} value={size}>{size}</option>
            ))}
          </select>
        </label>
      )}

      <button
        type="button"
        className="btn btn-outline btn-sm"
        disabled={safePage <= 1}
        onClick={() => onPageChange(safePage - 1)}
        aria-label="الصفحة السابقة"
      >
        السابق
      </button>
      <span aria-current="page">صفحة {safePage} من {totalPages}</span>
      <button
        type="button"
        className="btn btn-outline btn-sm"
        disabled={itemCount < pageSize || safePage >= totalPages}
        onClick={() => onPageChange(safePage + 1)}
        aria-label="الصفحة التالية"
      >
        التالي
      </button>
    </nav>
  );
}
