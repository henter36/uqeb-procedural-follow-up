export function StatusBadge({ active }: { active: boolean }) {
  return <span className={`badge ${active ? 'badge-green' : 'badge-gray'}`}>{active ? 'نشط' : 'غير نشط'}</span>;
}
