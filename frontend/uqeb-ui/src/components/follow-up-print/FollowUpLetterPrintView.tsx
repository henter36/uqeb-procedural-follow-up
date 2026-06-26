import { useCallback, useEffect, useRef } from 'react';

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

  const handlePrint = useCallback(async () => {
    if (printDisabled) return;
    await onPrint?.();
    iframeRef.current?.contentWindow?.print();
    printedRef.current = true;
  }, [onPrint, printDisabled]);

  useEffect(() => {
    printedRef.current = false;
  }, [html]);

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
        srcDoc={html}
        sandbox="allow-same-origin allow-modals"
        onLoad={handleFrameLoad}
      />
    </div>
  );
}
