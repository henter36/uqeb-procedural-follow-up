import type { ReactNode } from 'react';

type FormSectionProps = Readonly<{
  title: string;
  description?: string;
  children: ReactNode;
}>;

export default function FormSection({ title, description, children }: FormSectionProps) {
  return (
    <section className="form-section card section-card">
      <h3 className="form-section-title">{title}</h3>
      {description && <p className="form-section-desc">{description}</p>}
      {children}
    </section>
  );
}
