import type { ReactNode } from 'react';

type AlertVariant = 'error' | 'info' | 'success' | 'warning';

type AlertProps = {
  variant?: AlertVariant;
  children: ReactNode;
  role?: string;
};

export default function Alert({ variant = 'info', children, role }: AlertProps) {
  return (
    <div className={`alert alert-${variant}`} role={role ?? (variant === 'error' ? 'alert' : 'status')}>
      {children}
    </div>
  );
}
