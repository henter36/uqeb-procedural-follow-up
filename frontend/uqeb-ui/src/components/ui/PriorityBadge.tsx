import { priorityLabels } from '../../utils/labels';

const priorityClass: Record<string, string> = {
  Normal: 'badge-gray',
  Urgent: 'badge-orange',
  VeryUrgent: 'badge-red',
};

type PriorityBadgeProps = {
  priority: string;
};

export default function PriorityBadge({ priority }: PriorityBadgeProps) {
  return (
    <span className={`badge ${priorityClass[priority] ?? 'badge-gray'}`}>
      {priorityLabels[priority] || priority}
    </span>
  );
}
