import { useCallback, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { followUpPrintApi } from '../api/services';
import { getApiErrorMessage } from '../utils/apiHelpers';
import FollowUpLetterPrintView from '../components/follow-up-print/FollowUpLetterPrintView';
import { Alert, ErrorState, LoadingInline } from '../components/ui';
import { useDeferredEffect } from '../hooks/useDeferredEffect';
import { usePendingPrintSummary } from '../hooks/usePendingPrintSummary';

export default function FollowUpPrintPartPage() {
  const { jobId, partNumber } = useParams();
  const parsedJobId = Number(jobId);
  const parsedPartNumber = Number(partNumber);
  const [html, setHtml] = useState('');
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState('');
  const [printError, setPrintError] = useState('');
  const [marked, setMarked] = useState(false);
  const [printing, setPrinting] = useState(false);
  const { refresh } = usePendingPrintSummary();

  const loadPrintView = useCallback(async (active: () => boolean) => {
    if (!Number.isFinite(parsedJobId) || !Number.isFinite(parsedPartNumber)) {
      await Promise.resolve();
      if (active()) {
        setLoadError('معرف الجزء غير صالح');
        setLoading(false);
      }
      return;
    }
    await Promise.resolve();
    if (active()) {
      setLoading(true);
      setLoadError('');
    }
    try {
      const res = await followUpPrintApi.getPartPrintView(parsedJobId, parsedPartNumber);
      if (!active()) return;
      setHtml(res.data);
    } catch (err: unknown) {
      if (!active()) return;
      setLoadError(getApiErrorMessage(err));
    } finally {
      if (active()) setLoading(false);
    }
  }, [parsedJobId, parsedPartNumber]);

  useDeferredEffect(loadPrintView, [loadPrintView]);

  const handlePrint = async () => {
    if (marked || printing) return;
    setPrinting(true);
    setPrintError('');
    try {
      await followUpPrintApi.markPartPrintRequested(parsedJobId, parsedPartNumber);
      setMarked(true);
      await refresh();
    } catch (err: unknown) {
      setPrintError(getApiErrorMessage(err));
    } finally {
      setPrinting(false);
    }
  };

  if (loading) {
    return <LoadingInline label="جاري تحضير صفحة الطباعة..." />;
  }

  if (loadError) {
    return (
      <div dir="rtl">
        <div className="no-print mb-3">
          <div className="follow-up-print-top-bar">
            <Link to={`/follow-up-print/jobs/${parsedJobId}`} className="btn btn-outline">
              ← العودة للمهمة
            </Link>
          </div>
        </div>
        <ErrorState title="تعذر تحضير الطباعة" description={loadError} />
        <div className="form-actions mt-4">
          <button
            type="button"
            className="btn btn-outline"
            onClick={() => { loadPrintView(() => true).catch(() => undefined); }}
          >
            تحديث الحالة
          </button>
        </div>
      </div>
    );
  }

  return (
    <div dir="rtl">
      <div className="no-print mb-3">
        <div className="follow-up-print-top-bar">
          <Link to={`/follow-up-print/jobs/${parsedJobId}`} className="btn btn-outline">
            ← العودة للمهمة
          </Link>
          <Link to="/follow-up-print/pending" className="btn btn-outline">
            بانتظار التسجيل
          </Link>
        </div>

        {marked ? (
          <Alert variant="success">
            تم تسجيل طلب الطباعة. اذهب إلى «بانتظار التسجيل» لتأكيد الطباعة وتسجيل التعقيب.
          </Alert>
        ) : (
          <Alert variant="info">
            انقر «طباعة الآن» لفتح نافذة الطباعة، ثم اطبع أو احفظ PDF.
            بعد الطباعة اذهب إلى «بانتظار التسجيل» لتأكيد الطباعة وتسجيل التعقيب.
            {' '}قم بتعطيل «Headers and footers» من إعدادات الطباعة لمنع ظهور عنوان المتصفح وتاريخه على الخطاب.
          </Alert>
        )}
        {printError && <Alert variant="error">{printError}</Alert>}
      </div>
      <FollowUpLetterPrintView
        html={html}
        autoPrint={false}
        onPrint={handlePrint}
        printDisabled={marked || printing}
        printingLabel={marked ? 'تم تسجيل طلب الطباعة' : 'جاري تسجيل الطلب...'}
      />
    </div>
  );
}
