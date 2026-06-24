import { useMemo } from 'react';
import { sanitizeReportHtml } from '../utils/sanitizeReportHtml';
import '../styles/institutional-report/report.css';

type ReportPreviewDocumentProps = {
  htmlContent: string;
  stylesheet?: string | null;
  zoom?: number;
  title?: string;
};

export function ReportPreviewDocument({
  htmlContent,
  stylesheet,
  zoom = 0.75,
  title = 'معاينة صفحة التقرير',
}: ReportPreviewDocumentProps) {
  const sanitizedHtml = useMemo(() => sanitizeReportHtml(htmlContent), [htmlContent]);

  return (
    <section
      className="report-preview-frame"
      aria-label={title}
      style={{ transform: `scale(${zoom})`, transformOrigin: 'top center' }}
    >
      {stylesheet ? <style>{stylesheet}</style> : null}
      <div
        className="report-preview-document"
        dir="rtl"
        lang="ar"
        // eslint-disable-next-line react/no-danger
        dangerouslySetInnerHTML={{ __html: sanitizedHtml }}
      />
    </section>
  );
}
