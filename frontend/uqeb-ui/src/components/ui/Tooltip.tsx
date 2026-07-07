import { useId, type ReactNode } from 'react';

type TooltipProps = Readonly<{
  content: string;
  children?: ReactNode;
  className?: string;
  triggerLabel?: string;
}>;

export default function Tooltip({
  content,
  children,
  className,
  triggerLabel = 'عرض التوضيح',
}: TooltipProps) {
  const id = useId();
  const wrapperClassName = ['tooltip-wrapper', className].filter(Boolean).join(' ');

  return (
    <span className={wrapperClassName}>
      {children}
      <button
        type="button"
        className="tooltip-trigger"
        aria-describedby={id}
        aria-label={triggerLabel}
      >
        <span className="metric-hint-icon" aria-hidden="true">ⓘ</span>
      </button>
      <span role="tooltip" id={id} className="tooltip-bubble">
        {content}
      </span>
    </span>
  );
}
