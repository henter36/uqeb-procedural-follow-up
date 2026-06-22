import type { FormEvent, ReactNode } from 'react';

type FormModalProps = {
  title: string;
  children: ReactNode;
  onClose: () => void;
  onSubmit: (e: FormEvent) => void;
  submitting: boolean;
  submitLabel: string;
};

export function FormModal({
  title,
  children,
  onClose,
  onSubmit,
  submitting,
  submitLabel,
}: Readonly<FormModalProps>) {
  return (
    <div className="modal-overlay" role="presentation">
      <div className="modal" role="dialog" aria-modal="true" aria-labelledby="form-modal-title">
        <h3 id="form-modal-title">{title}</h3>
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
