import { useCallback, useEffect, useRef, useState } from 'react';
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
  const pollSeq = useRef(0);
  const isTerminalStatus = job ? ['Completed', 'Cancelled', 'Expired', 'Failed'].includes(job.status) : false;

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
    if (isTerminalStatus) return undefined;
    let mounted = true;
    const timer = globalThis.setInterval(() => {
      const seq = pollSeq.current + 1;
      pollSeq.current = seq;
      followUpPrintApi.getJob(jobId)
        .then((res) => {
          if (mounted && pollSeq.current === seq) setJob(res.data);
        })
        .catch(() => undefined);
    }, 5000);
    return () => {
      mounted = false;
      pollSeq.current += 1;
      globalThis.clearInterval(timer);
    };
  }, [isTerminalStatus, jobId]);

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
  const getUnavailablePrintReason = (status: string) => {
    if (status === 'Cancelled') return 'الجزء ملغى ولا يمكن طباعته.';
    if (status === 'Failed') return 'فشل تجهيز الجزء.';
    if (status === 'Printed') return 'تم طلب الطباعة لهذا الجزء؛ يمكن فتحه للمراجعة فقط.';
    return 'الجزء غير جاهز للطباعة بعد.';
  };

  return (
    <div dir="rtl">
      <PageHeader
        title={`مهمة الطباعة #${job.id}`}
        subtitle="تفاصيل المهمة وأجزاء الطباعة"
        actions={<Link to="/follow-up-print/jobs" className="btn btn-outline">العودة للمهام</Link>}
      />

      {message && <Alert variant="success">{message}</Alert>}
      {error && <Alert variant="error">{error}</Alert>}

      {/* دليل الخطوات للمستخدم */}
      {job.status === 'ReadyToPrint' || job.status === 'PartiallyPrinted' ? (
        <Alert variant="success">
          تم تجهيز الخطابات ويمكن طباعتها الآن — افتح الجزء الجاهز وانقر «طباعة الآن».
        </Alert>
      ) : job.status === 'Completed' ? (
        <Alert variant="info">
          اكتملت المهمة. يمكن مراجعة سجلات الطباعة من صفحة «بانتظار التسجيل».
        </Alert>
      ) : (
        <Alert variant="info">
          تقوم المهمة بتحضير الخطابات. عند اكتمال الجزء ستظهر زر «طباعة الآن».
        </Alert>
      )}

      <div className="card">
        <div className="details-banner-row">
          <span className={`badge ${followUpPrintJobStatusBadgeClass(job.status)}`}>
            {followUpPrintJobStatusLabels[job.status]}
          </span>
          <span>المعاملات: {job.totalTransactions}</span>
          <span>الخطابات الجاهزة: {job.readyLetters}/{job.totalLetters}</span>
          <span>الأجزاء التي طُلبت طباعتها: {job.printedParts}/{job.totalParts}</span>
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
                <th>طلب الطباعة</th>
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
                    {['ReadyToPrint', 'PartiallyReady'].includes(part.status) ? (
                      <Link
                        to={`/follow-up-print/parts/${job.id}/${part.partNumber}/print`}
                        className="btn btn-sm btn-primary"
                        target="_blank"
                        rel="noreferrer"
                      >
                        طباعة الآن
                      </Link>
                    ) : part.status === 'Printed' ? (
                      <Link
                        to={`/follow-up-print/parts/${job.id}/${part.partNumber}/print`}
                        className="btn btn-sm btn-outline"
                        target="_blank"
                        rel="noreferrer"
                      >
                        عرض (مطبوع)
                      </Link>
                    ) : (
                      <span className="text-muted">{getUnavailablePrintReason(part.status)}</span>
                    )}
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
