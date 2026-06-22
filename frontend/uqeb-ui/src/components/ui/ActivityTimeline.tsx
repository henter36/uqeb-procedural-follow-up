import DateDisplay from '../DateDisplay';

export type TimelineEvent = {
  id: string | number;
  action: string;
  userName?: string;
  date: string;
  detail?: string;
};

type ActivityTimelineProps = {
  events: TimelineEvent[];
  emptyLabel?: string;
};

export default function ActivityTimeline({ events, emptyLabel = 'لا توجد أحداث' }: ActivityTimelineProps) {
  if (events.length === 0) {
    return <p className="text-muted text-center">{emptyLabel}</p>;
  }

  return (
    <div className="activity-timeline" role="list" aria-label="السجل الزمني">
      {events.map((event) => (
        <div key={event.id} className="timeline-item" role="listitem">
          <div className="timeline-dot" aria-hidden="true" />
          <div className="timeline-item-content">
            <div className="timeline-item-header">
              <span className="timeline-item-action">{event.action}</span>
              <span className="timeline-item-date">
                <DateDisplay date={event.date} />
              </span>
            </div>
            {event.userName && (
              <div className="timeline-item-user">بواسطة: {event.userName}</div>
            )}
            {event.detail && (
              <div className="timeline-item-detail">{event.detail}</div>
            )}
          </div>
        </div>
      ))}
    </div>
  );
}
