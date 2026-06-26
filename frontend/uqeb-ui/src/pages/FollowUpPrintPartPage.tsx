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
  const [error, setError] = useState('');
  const [marked, setMarked] = useState(false);
  const [printing, setPrinting] = useState(false);
  const { refresh } = usePendingPrintSummary();

  const loadPrintView = useCallback(async (active: () => boolean) => {
    if (!Number.isFinite(parsedJobId) || !Number.isFinite(parsedPartNumber)) {
      await Promise.resolve();
      if (active()) {
        setError('معرف الجزء غير صالح');
        setLoading(false);
      }
      return;
    }
    await Promise.resolve();
    if (active()) {
      setLoading(true);
      setError('');
    }
    try {
      const res = await followUpPrintApi.getPartPrintView(parsedJobId, parsedPartNumber);
      if (!active()) return;
      setHtml(res.data);
    } catch (err: unknown) {
      if (!active()) return;
      setError(getApiErrorMessage(err));
    } finally {
      if (active()) setLoading(false);
    }
  }, [parsedJobId, parsedPartNumber]);

  useDeferredEffect(loadPrintView, [loadPrintView]);

  const handlePrint = async () => {
    if (marked || printing) return;
    setPrinting(true);
    setError('');
    try {
      await followUpPrintApi.markPartPrintRequested(parsedJobId, parsedPartNumber);
      setMarked(true);
      await refresh();
    } catch (err: unknown) {
      setError(getApiErrorMessage(err));
      throw err;
    } finally {
      setPrinting(false);
    }
  };

  if (loading) {
    return <LoadingInline label="جاري تحضير صفحة الطباعة..." />;
  }

  if (error) {
    return <ErrorState title="تعذر تحضير الطباعة" description={error} />;
  }

  return (
    <div dir="rtl">
      <div className="no-print mb-3">
        <Link to={`/follow-up-print/jobs/${parsedJobId}`} className="btn btn-outline">العودة للمهمة</Link>
        {error && <Alert variant="error">{error}</Alert>}
      </div>
      <FollowUpLetterPrintView
        html={html}
        autoPrint={false}
        onPrint={handlePrint}
        printDisabled={marked || printing}
        printingLabel={marked ? 'تم التسجيل' : 'جاري التسجيل...'}
      />
    </div>
  );
}
