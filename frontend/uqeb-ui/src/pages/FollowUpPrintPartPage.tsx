import { useCallback, useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { followUpPrintApi } from '../api/services';
import { getApiErrorMessage } from '../utils/apiHelpers';
import FollowUpLetterPrintView from '../components/follow-up-print/FollowUpLetterPrintView';
import { Alert, ErrorState, LoadingInline } from '../components/ui';

export default function FollowUpPrintPartPage() {
  const { jobId, partNumber } = useParams();
  const parsedJobId = Number(jobId);
  const parsedPartNumber = Number(partNumber);
  const [html, setHtml] = useState('');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [marked, setMarked] = useState(false);

  const loadPrintView = useCallback(async () => {
    if (!Number.isFinite(parsedJobId) || !Number.isFinite(parsedPartNumber)) {
      setError('معرف الجزء غير صالح');
      setLoading(false);
      return;
    }
    setLoading(true);
    setError('');
    try {
      const res = await followUpPrintApi.getPartPrintView(parsedJobId, parsedPartNumber);
      setHtml(res.data);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err));
    } finally {
      setLoading(false);
    }
  }, [parsedJobId, parsedPartNumber]);

  useEffect(() => {
    void loadPrintView();
  }, [loadPrintView]);

  const handlePrint = async () => {
    if (marked) return;
    try {
      await followUpPrintApi.markPartPrintRequested(parsedJobId, parsedPartNumber);
      setMarked(true);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err));
    }
  };

  if (loading) {
    return <LoadingInline label="جاري تحضير صفحة الطباعة..." />;
  }

  if (error && !html) {
    return (
      <div dir="rtl" className="follow-up-print-part-page">
        <ErrorState title="تعذر تحميل صفحة الطباعة" description={error} />
        <Link to={`/follow-up-print/jobs/${parsedJobId}`} className="btn btn-outline">العودة للمهمة</Link>
      </div>
    );
  }

  return (
    <div dir="rtl" className="follow-up-print-part-page">
      {error && <Alert variant="error">{error}</Alert>}
      <FollowUpLetterPrintView
        html={html}
        title={`طباعة الجزء ${parsedPartNumber} — مهمة ${parsedJobId}`}
        onPrint={() => { void handlePrint(); }}
      />
    </div>
  );
}
