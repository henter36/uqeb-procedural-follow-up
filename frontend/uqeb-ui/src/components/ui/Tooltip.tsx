import { useId, type ReactNode } from 'react';

type TooltipProps = Readonly<{
  content: string;
  children: ReactNode;
  className?: string;
}>;

export default function Tooltip({ content, children, className }: TooltipProps) {
  const id = useId();

  return (
    <span
      className={`tooltip-wrapper${className ? ` ${className}` : ''}`}
      tabIndex={0}
      aria-describedby={id}
    >
      {children}
      <span role="tooltip" id={id} className="tooltip-bubble">
        {content}
      </span>
    </span>
  );
}
