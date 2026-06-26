import { useCallback, useEffect, useMemo, useRef } from 'react';
import { sanitizeFullDocumentHtml } from '../../utils/sanitizePrintHtml';

type FollowUpLetterPrintViewProps = Readonly<{
  html: string;
  title?: string;
  autoPrint?: boolean;
  onPrint?: () => void | Promise<void>;
  printDisabled?: boolean;
  printingLabel?: string;
}>;

export default function FollowUpLetterPrintView({
  html,
  title = 'طباعة خطابات التعقيب',
  autoPrint = false,
  onPrint,
  printDisabled = false,
  printingLabel = 'جاري التسجيل...',
}: FollowUpLetterPrintViewProps) {
  const iframeRef = useRef<HTMLIFrameElement | null>(null);
  const printedRef = useRef(false);
  const sanitizedHtml = useMemo(() => sanitizeFullDocumentHtml(html), [html]);

  const handlePrint = useCallback(async () => {
    if (printDisabled) return;
    await onPrint?.();
    iframeRef.current?.contentWindow?.print();
    printedRef.current = true;
  }, [onPrint, printDisabled]);

  useEffect(() => {
    printedRef.current = false;
  }, [sanitizedHtml]);

  const handleFrameLoad = useCallback(() => {
    if (!autoPrint || printedRef.current) return;
    printedRef.current = true;
    handlePrint().catch(() => {
      printedRef.current = false;
    });
  }, [autoPrint, handlePrint]);

  return (
    <div className="follow-up-letter-print-view" dir="rtl">
      <div className="follow-up-print-toolbar no-print">
        <h2>{title}</h2>
        <button type="button" className="btn btn-primary" disabled={printDisabled} onClick={() => { handlePrint().catch(() => undefined); }}>
          {printDisabled ? printingLabel : 'طباعة'}
        </button>
      </div>
      <iframe
        ref={iframeRef}
        title={title}
        className="follow-up-print-frame"
        srcDoc={sanitizedHtml}
        sandbox="allow-same-origin allow-modals"
        onLoad={handleFrameLoad}
      />
    </div>
  );
}
