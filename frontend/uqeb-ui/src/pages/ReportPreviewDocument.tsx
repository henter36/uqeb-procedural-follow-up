import { useMemo } from 'react';
import { sanitizeReportHtml } from '../utils/sanitizeReportHtml';
import { buildPreviewDocument } from './reportPreviewHtml';

type ReportPreviewDocumentProps = Readonly<{
  htmlContent: string;
  stylesheet?: string | null;
  zoom?: number;
  title?: string;
}>;

export function ReportPreviewDocument({
  htmlContent,
  stylesheet,
  zoom = 0.75,
  title = 'معاينة صفحة التقرير',
}: ReportPreviewDocumentProps) {
  const sanitizedHtml = useMemo(() => sanitizeReportHtml(htmlContent), [htmlContent]);
  const previewDocument = useMemo(
    () => buildPreviewDocument(stylesheet ?? '', sanitizedHtml),
    [stylesheet, sanitizedHtml],
  );

  return (
    <div
      className="report-preview-frame"
      style={{ transform: `scale(${zoom})`, transformOrigin: 'top center' }}
    >
      <iframe
        title={title}
        className="report-preview-iframe"
        sandbox=""
        srcDoc={previewDocument}
      />
    </div>
  );
}
