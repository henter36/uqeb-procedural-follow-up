import type { FormEvent, ReactNode } from 'react';

export function FormModal({
  title,
  children,
  onClose,
  onSubmit,
  submitting,
  submitLabel,
}: {
  title: string;
  children: ReactNode;
  onClose: () => void;
  onSubmit: (e: FormEvent) => void;
  submitting: boolean;
  submitLabel: string;
}) {
  return (
    <div className="modal-overlay">
      <div className="modal">
        <h3>{title}</h3>
        <form onSubmit={onSubmit}>
          {children}
          <div className="form-actions">
            <button type="submit" className="btn btn-primary" disabled={submitting}>
              {submitting ? 'جاري الحفظ...' : submitLabel}
            </button>
            <button type="button" className="btn btn-outline" onClick={onClose} disabled={submitting}>إلغاء</button>
          </div>
        </form>
      </div>
    </div>
  );
}
