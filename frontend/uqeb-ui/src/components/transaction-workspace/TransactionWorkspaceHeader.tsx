import type { TransactionDetail } from '../../api/types';
import DateDisplay from '../DateDisplay';
import { StatusBadge, PriorityBadge } from '../ui';
import DepartmentBadges from '../DepartmentBadges';
import { formatDaysSince } from '../../utils/responseTiming';

type TransactionWorkspaceHeaderProps = Readonly<{
  tx: TransactionDetail;
}>;

function formatDaysRemaining(days?: number): string {
  if (days === undefined || days === null) return '—';
  if (days < 0) return `متأخر ${Math.abs(days)} يوم`;
  if (days === 0) return 'اليوم';
  return `${days} يوم`;
}

export default function TransactionWorkspaceHeader({ tx }: TransactionWorkspaceHeaderProps) {
  return (
    <header className="workspace-header card">
      <div className="workspace-header-primary">
        <div className="workspace-header-title-row">
          <h2 className="workspace-header-number">{tx.incomingNumber}</h2>
          <StatusBadge status={tx.status} isOverdue={tx.isOverdue} />
          <PriorityBadge priority={tx.priority} />
          {tx.isOverdue && <span className="badge badge-red">متأخرة</span>}
          {tx.hasPendingAssignments && <span className="badge badge-orange">باقي إجراء</span>}
        </div>
        <p className="workspace-header-subject">{tx.subject}</p>
      </div>

      <dl className="workspace-header-metrics">
        <div>
          <dt>الجهة الوارد منها</dt>
          <dd>{tx.incomingFrom || '—'}</dd>
        </div>
        <div>
          <dt>الإدارات</dt>
          <dd><DepartmentBadges names={tx.outgoingDepartmentNames} /></dd>
        </div>
        <div>
          <dt>منذ ورود المعاملة</dt>
          <dd>{formatDaysSince(tx.daysSinceIncoming, '0')}</dd>
        </div>
        <div>
          <dt>الأيام المتبقية للرد</dt>
          <dd className={tx.isResponseOverdue ? 'text-danger' : undefined}>
            {formatDaysRemaining(tx.daysRemainingForResponse)}
          </dd>
        </div>
        <div>
          <dt>تاريخ الرد المطلوب</dt>
          <dd>{tx.responseDueDate ? <DateDisplay date={tx.responseDueDate} /> : '—'}</dd>
        </div>
        <div>
          <dt>منذ آخر تعقيب</dt>
          <dd>{formatDaysSince(tx.daysSinceLastFollowUp)}</dd>
        </div>
      </dl>
    </header>
  );
}
