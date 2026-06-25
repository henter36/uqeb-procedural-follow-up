import { useCallback, useEffect, useState } from 'react';
import { Link, useParams } from 'react-router-dom';
import { followUpPrintApi } from '../api/services';
import type { FollowUpPrintJob } from '../api/types';
import { getApiErrorMessage } from '../utils/apiHelpers';
import {
  followUpPrintJobStatusBadgeClass,
  followUpPrintJobStatusLabels,
  followUpPrintJobPartStatusLabels,
  followUpPrintPartStatusBadgeClass,
} from '../utils/followUpPrintLabels';
import DateDisplay from '../components/DateDisplay';
import {
  Alert, ErrorState, LoadingInline, PageHeader,
} from '../components/ui';
import { useDeferredEffect } from '../hooks/useDeferredEffect';

export default function FollowUpPrintJobDetailPage() {
  const { id } = useParams();
  const jobId = Number(id);
  const [job, setJob] = useState<FollowUpPrintJob | null>(null);
  const [loading, setLoading] = useState(true);
  const [acting, setActing] = useState(false);
  const [error, setError] = useState('');
  const [message, setMessage] = useState('');

  const loadJob = useCallback(async (active: () => boolean) => {
    if (!Number.isFinite(jobId)) {
      await Promise.resolve();
      if (active()) {
        setError('معرف المهمة غير صالح');
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
      const res = await followUpPrintApi.getJob(jobId);
      if (!active()) return;
      setJob(res.data);
    } catch (err: unknown) {
      if (!active()) return;
      setError(getApiErrorMessage(err));
      setJob(null);
    } finally {
      if (active()) setLoading(false);
    }
  }, [jobId]);

  useDeferredEffect(loadJob, [loadJob]);

  useEffect(() => {
    if (!Number.isFinite(jobId)) return undefined;
    const timer = globalThis.setInterval(() => {
      followUpPrintApi.getJob(jobId)
        .then((res) => setJob(res.data))
        .catch(() => undefined);
    }, 5000);
    return () => globalThis.clearInterval(timer);
  }, [jobId]);

  const handleCancel = async () => {
    if (!globalThis.confirm('هل أنت متأكد من إلغاء مهمة الطباعة؟')) return;
    setActing(true);
    setError('');
    try {
      const res = await followUpPrintApi.cancelJob(jobId);
      setJob(res.data);
      setMessage('تم إلغاء المهمة.');
    } catch (err: unknown) {
      setError(getApiErrorMessage(err));
    } finally {
      setActing(false);
    }
  };

  const handleRetry = async () => {
    setActing(true);
    setError('');
    try {
      const res = await followUpPrintApi.retryJob(jobId);
      setJob(res.data);
      setMessage('تمت إعادة محاولة المهمة.');
    } catch (err: unknown) {
      setError(getApiErrorMessage(err));
    } finally {
      setActing(false);
    }
  };

  if (loading) {
    return (
      <div dir="rtl">
        <PageHeader title="تفاصيل مهمة الطباعة" />
        <LoadingInline label="جاري التحميل..." />
      </div>
    );
  }

  if (error && !job) {
    return (
      <div dir="rtl">
        <PageHeader title="تفاصيل مهمة الطباعة" />
        <ErrorState title="تعذر التحميل" description={error} />
      </div>
    );
  }

  if (!job) return null;

  const canCancel = !['Completed', 'Cancelled', 'Expired'].includes(job.status);
  const canRetry = job.status === 'Failed';

  return (
    <div dir="rtl">
      <PageHeader
        title={`مهمة الطباعة #${job.id}`}
        subtitle="تفاصيل المهمة وأجزاء الطباعة"
        actions={<Link to="/follow-up-print/jobs" className="btn btn-outline">العودة للمهام</Link>}
      />

      {message && <Alert variant="success">{message}</Alert>}
      {error && <Alert variant="error">{error}</Alert>}

      <div className="card">
        <div className="details-banner-row">
          <span className={`badge ${followUpPrintJobStatusBadgeClass(job.status)}`}>
            {followUpPrintJobStatusLabels[job.status]}
          </span>
          <span>المعاملات: {job.totalTransactions}</span>
          <span>الخطابات الجاهزة: {job.readyLetters}/{job.totalLetters}</span>
          <span>الأجزاء المطبوعة: {job.printedParts}/{job.totalParts}</span>
          <span>تاريخ الإنشاء: <DateDisplay date={job.createdAt} /></span>
        </div>
        {job.failureReason && <Alert variant="error">{job.failureReason}</Alert>}
        <div className="form-actions mt-4">
          {canCancel && (
            <button type="button" className="btn btn-outline" disabled={acting} onClick={() => { handleCancel().catch(() => undefined); }}>
              إلغاء المهمة
            </button>
          )}
          {canRetry && (
            <button type="button" className="btn btn-secondary" disabled={acting} onClick={() => { handleRetry().catch(() => undefined); }}>
              إعادة المحاولة
            </button>
          )}
        </div>
      </div>

      <div className="card mt-4">
        <h3>أجزاء الطباعة</h3>
        <div className="table-wrapper table-wrapper-spaced">
          <table className="data-table">
            <thead>
              <tr>
                <th>الجزء</th>
                <th>الحالة</th>
                <th>عدد الخطابات</th>
                <th>الصفحات</th>
                <th>جاهز</th>
                <th>طُبع</th>
                <th>إجراء</th>
              </tr>
            </thead>
            <tbody>
              {job.parts.map((part) => (
                <tr key={part.id}>
                  <td>{part.partNumber}</td>
                  <td>
                    <span className={`badge ${followUpPrintPartStatusBadgeClass(part.status)}`}>
                      {followUpPrintJobPartStatusLabels[part.status]}
                    </span>
                  </td>
                  <td>{part.letterCount}</td>
                  <td>{part.estimatedPages}</td>
                  <td>{part.readyAt ? <DateDisplay date={part.readyAt} /> : '—'}</td>
                  <td>{part.printedAt ? <DateDisplay date={part.printedAt} /> : '—'}</td>
                  <td>
                    {part.status === 'ReadyToPrint' || part.status === 'Printed' ? (
                      <Link
                        to={`/follow-up-print/parts/${job.id}/${part.partNumber}/print`}
                        className="btn btn-sm btn-primary"
                        target="_blank"
                        rel="noreferrer"
                      >
                        طباعة
                      </Link>
                    ) : '—'}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}
