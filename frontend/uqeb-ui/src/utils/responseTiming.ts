export function responseTimingBadgeClass(status?: string): string {
  switch (status) {
    case 'remaining': return 'badge-blue';
    case 'due_today': return 'badge-yellow';
    case 'overdue': return 'badge-red';
    case 'completed': return 'badge-green';
    default: return 'badge-gray';
  }
}

export function formatDaysSince(value?: number | null, empty = '—'): string {
  if (value == null) return empty;
  if (value === 0) return 'اليوم';
  return `${value} يوم`;
}

export function formatCompletionDays(days?: number | null): string {
  if (days == null) return '—';
  if (days === 0) return 'نفس اليوم';
  if (days === 1) return 'يوم واحد';
  if (days === 2) return 'يومان';
  return `${days} أيام`;
}
