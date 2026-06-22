import { statusLabels, statusBadgeClass } from '../../utils/labels';

type StatusBadgeProps = {
  status: string;
  isOverdue?: boolean;
};

export default function StatusBadge({ status, isOverdue }: StatusBadgeProps) {
  return (
    <span className={`badge ${statusBadgeClass(status, isOverdue)}`}>
      {statusLabels[status] || status}
    </span>
  );
}
