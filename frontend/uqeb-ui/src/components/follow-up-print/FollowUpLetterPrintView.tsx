import { useCallback, useEffect } from 'react';

type FollowUpLetterPrintViewProps = Readonly<{
  html: string;
  title?: string;
  autoPrint?: boolean;
  onPrint?: () => void;
}>;

export default function FollowUpLetterPrintView({
  html,
  title = 'طباعة خطابات التعقيب',
  autoPrint = false,
  onPrint,
}: FollowUpLetterPrintViewProps) {
  const handlePrint = useCallback(() => {
    window.print();
    onPrint?.();
  }, [onPrint]);

  useEffect(() => {
    if (!autoPrint) return undefined;
    const timer = window.setTimeout(() => {
      window.print();
      onPrint?.();
    }, 400);
    return () => window.clearTimeout(timer);
  }, [autoPrint, html, onPrint]);

  return (
    <div className="follow-up-letter-print-view" dir="rtl">
      <div className="follow-up-print-toolbar no-print">
        <h2>{title}</h2>
        <button type="button" className="btn btn-primary" onClick={handlePrint}>
          طباعة
        </button>
      </div>
      <iframe
        title={title}
        className="follow-up-print-frame"
        srcDoc={html}
        sandbox="allow-same-origin allow-modals"
      />
    </div>
  );
}
