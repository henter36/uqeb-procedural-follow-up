type StatusBadgeProps = {
  active: boolean;
};

export function StatusBadge({ active }: Readonly<StatusBadgeProps>) {
  return <span className={`badge ${active ? 'badge-green' : 'badge-gray'}`}>{active ? 'نشط' : 'غير نشط'}</span>;
}
