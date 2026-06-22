import type { SVGProps } from 'react';

type IconProps = SVGProps<SVGSVGElement>;

const defaults: IconProps = { width: 20, height: 20, fill: 'none', stroke: 'currentColor', strokeWidth: 1.75, strokeLinecap: 'round' as const, strokeLinejoin: 'round' as const };

export function IconDashboard(props: IconProps) {
  return (
    <svg viewBox="0 0 24 24" {...defaults} {...props}>
      <rect x="3" y="3" width="7" height="9" rx="1" />
      <rect x="14" y="3" width="7" height="5" rx="1" />
      <rect x="14" y="12" width="7" height="9" rx="1" />
      <rect x="3" y="16" width="7" height="5" rx="1" />
    </svg>
  );
}

export function IconTransactions(props: IconProps) {
  return (
    <svg viewBox="0 0 24 24" {...defaults} {...props}>
      <path d="M9 5H7a2 2 0 0 0-2 2v12a2 2 0 0 0 2 2h10a2 2 0 0 0 2-2V7a2 2 0 0 0-2-2h-2" />
      <rect x="9" y="3" width="6" height="4" rx="1" />
      <path d="M9 12h6M9 16h4" />
    </svg>
  );
}

export function IconReports(props: IconProps) {
  return (
    <svg viewBox="0 0 24 24" {...defaults} {...props}>
      <path d="M3 3v18h18" />
      <path d="M7 16l4-4 4 4 5-6" />
    </svg>
  );
}

export function IconUsers(props: IconProps) {
  return (
    <svg viewBox="0 0 24 24" {...defaults} {...props}>
      <circle cx="9" cy="7" r="3" />
      <path d="M3 21v-1a4 4 0 0 1 4-4h4a4 4 0 0 1 4 4v1" />
      <path d="M16 3.13a4 4 0 0 1 0 7.75M21 21v-1a4 4 0 0 0-3-3.87" />
    </svg>
  );
}

export function IconSettings(props: IconProps) {
  return (
    <svg viewBox="0 0 24 24" {...defaults} {...props}>
      <circle cx="12" cy="12" r="3" />
      <path d="M12 1v2M12 21v2M4.22 4.22l1.42 1.42M18.36 18.36l1.42 1.42M1 12h2M21 12h2M4.22 19.78l1.42-1.42M18.36 5.64l1.42-1.42" />
    </svg>
  );
}

export function IconImport(props: IconProps) {
  return (
    <svg viewBox="0 0 24 24" {...defaults} {...props}>
      <path d="M12 3v12M8 11l4 4 4-4" />
      <path d="M4 17v2a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-2" />
    </svg>
  );
}

export function IconSecurity(props: IconProps) {
  return (
    <svg viewBox="0 0 24 24" {...defaults} {...props}>
      <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z" />
    </svg>
  );
}

export function IconLetter(props: IconProps) {
  return (
    <svg viewBox="0 0 24 24" {...defaults} {...props}>
      <path d="M4 4h16v16H4z" />
      <path d="M4 8h16M8 12h8M8 16h5" />
    </svg>
  );
}

export function IconChevron(props: IconProps & { direction?: 'left' | 'right' }) {
  const { direction = 'right', ...rest } = props;
  return (
    <svg viewBox="0 0 24 24" {...defaults} {...rest}>
      {direction === 'right' ? <path d="M9 18l6-6-6-6" /> : <path d="M15 18l-6-6 6-6" />}
    </svg>
  );
}

export function IconMenu(props: IconProps) {
  return (
    <svg viewBox="0 0 24 24" {...defaults} {...props}>
      <path d="M4 6h16M4 12h16M4 18h16" />
    </svg>
  );
}
