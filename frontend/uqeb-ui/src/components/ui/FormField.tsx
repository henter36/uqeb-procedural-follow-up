import type { ReactNode } from 'react';

type FormFieldProps = {
  label: string;
  htmlFor?: string;
  required?: boolean;
  error?: string;
  children: ReactNode;
};

export default function FormField({ label, htmlFor, required, error, children }: FormFieldProps) {
  return (
    <div className="form-group">
      <label className="form-field-label" htmlFor={htmlFor}>
        {required && <span className="form-field-required" aria-hidden="true">*</span>}
        {label}
      </label>
      {children}
      {error && <span className="field-error" role="alert">{error}</span>}
    </div>
  );
}
