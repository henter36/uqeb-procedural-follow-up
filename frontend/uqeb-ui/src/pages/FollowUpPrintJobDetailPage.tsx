import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react';
import { Link, useParams } from 'react-router-dom';
import { followUpPrintApi } from '../api/services';
import type { FollowUpPrintJob, FollowUpPrintJobPart, FollowUpPrintJobStatus } from '../api/types';
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

function getPartStatusLabel(status: string): string {
  const known = followUpPrintJobPartStatusLabels[status as keyof typeof followUpPrintJobPartStatusLabels];
  return known ?? `حالة غير معروفة: ${status}`;
}

function getPartStatusBadgeClass(status: string): string {
  return followUpPrintPartStatusBadgeClass(status as keyof typeof followUpPrintJobPartStatusLabels);
}

function isPartPrintable(part: FollowUpPrintJobPart): boolean {
  if (['ReadyToPrint', 'PartiallyReady'].includes(part.status)) return true;
  // Defensive: treat as ready if readyAt is set and there are letters, even if status wasn't updated.
  if (part.readyAt && part.letterCount > 0 && !['Printed', 'Failed', 'Cancelled'].includes(part.status)) {
    return true;
  }
  return false;
}

function getUnavailablePrintReason(status: string): string {
  if (status === 'Queued') return 'في انتظار بدء المعالجة.';
  if (status === 'Processing') return 'جارٍ التجهيز — يرجى الانتظار.';
  if (status === 'Failed') return 'فشل تجهيز الجزء. أعد محاولة المهمة.';
  if (status === 'Cancelled') return 'الجزء ملغى ولا يمكن طباعته.';
  if (status === 'Printed') return 'تم طلب الطباعة لهذا الجزء؛ يمكن فتحه للمراجعة فقط.';
  return 'الجزء غير جاهز للطباعة بعد.';
}

function renderPartAction(jobId: number, part: FollowUpPrintJobPart): ReactNode {
  if (isPartPrintable(part)) {
    return (
      <Link
        to={`/follow-up-print/parts/${jobId}/${part.partNumber}/print`}
        className="btn btn-sm btn-primary"
        target="_blank"
        rel="noreferrer"
      >
        طباعة الآن
      </Link>
    );
  }
  if (part.status === 'Printed') {
    return (
      <Link
        to={`/follow-up-print/parts/${jobId}/${part.partNumber}/print`}
        className="btn btn-sm btn-outline"
        target="_blank"
        rel="noreferrer"
      >
        عرض (مطبوع)
      </Link>
    );
  }
  return <span className="text-muted">{getUnavailablePrintReason(part.status)}</span>;
}

const STALE_THRESHOLD_MS = 30 * 60 * 1000; // 30 minutes
const STALE_PAGE_GRACE_MS = 2 * 60 * 1000; // 2 minutes

function hasReliableCreatedAt(createdAt: string, now: number): number | null {
  if (!/(?:[zZ]|[+-]\d{2}:\d{2})$/.test(createdAt)) return null;
  const parsed = Date.parse(createdAt);
  if (!Number.isFinite(parsed)) return null;
  if (now - parsed < 0) return null;
  return parsed;
}

function hasJobProgress(job: FollowUpPrintJob): boolean {
  if (job.processedLetters > 0 || job.readyLetters > 0 || job.failedLetters > 0 || job.skippedLetters > 0) {
    return true;
  }

  if (job.readyParts > 0 || job.printedParts > 0) {
    return true;
  }

  return job.parts.some((part) =>
    Boolean(part.readyAt)
    || Boolean(part.printedAt)
    || ['ReadyToPrint', 'PartiallyReady', 'Printed'].includes(part.status));
}

function isJobStale(job: FollowUpPrintJob, pageOpenedAt: number | null): boolean {
  if (pageOpenedAt === null) return false;

  const now = Date.now();

  if (now - pageOpenedAt < STALE_PAGE_GRACE_MS) return false;

  if (!(['Queued', 'Claimed', 'Processing'] as FollowUpPrintJobStatus[]).includes(job.status)) return false;

  if (hasJobProgress(job)) return false;

  const createdAt = hasReliableCreatedAt(job.createdAt, now);
  return createdAt !== null && now - createdAt > STALE_THRESHOLD_MS;
}

function getJobGuidanceAlert(
  status: FollowUpPrintJobStatus,
  job: FollowUpPrintJob,
): { variant: 'success' | 'info' | 'error'; message: string } {
  // Data-driven fallback: if letters are all ready, show success regardless of status string.
  const lettersAllReady = job.totalLetters > 0 && job.readyLetters >= job.totalLetters;
  const hasReadyPart = job.parts.some((p) => isPartPrintable(p));

  if (status === 'ReadyToPrint' || status === 'PartiallyPrinted') {
    return {
      variant: 'success',
      message: 'تم تجهيز الخطابات ويمكن طباعتها الآن — افتح الجزء الجاهز وانقر «طباعة الآن».',
    };
  }
  if (status === 'Completed') {
    return {
      variant: 'info',
      message: 'اكتملت المهمة. يمكن مراجعة سجلات الطباعة من صفحة «بانتظار تسجيل التعقيب».',
    };
  }
  if (status === 'Failed') {
    return {
      variant: 'error',
      message: 'فشلت المهمة. اضغط «إعادة المحاولة» للمحاولة مجددًا، أو راجع سبب الفشل أدناه.',
    };
  }
  if (status === 'Cancelled') {
    return { variant: 'info', message: 'تم إلغاء المهمة.' };
  }
  if (status === 'Expired') {
    return { variant: 'error', message: 'انتهت صلاحية المهمة ولم تُطبع. أعد إنشاء المهمة إذا لزم الأمر.' };
  }
  // Queued or Processing — but data says letters are ready.
  if (lettersAllReady && hasReadyPart) {
    return {
      variant: 'success',
      message: 'تم تجهيز الخطابات. افتح الجزء الجاهز واضغط «طباعة الآن».',
    };
  }
  if (status === 'Queued') {
    return {
      variant: 'info',
      message: 'المهمة في طابور الانتظار — ستبدأ المعالجة قريبًا. يمكنك تحديث الحالة يدويًا.',
    };
  }
  if (status === 'Processing') {
    return {
      variant: 'info',
      message: 'جارٍ تجهيز الخطابات — سيظهر زر «طباعة الآن» عند اكتمال الجزء.',
    };
  }
  return {
    variant: 'info',
    message: 'تقوم المهمة بتحضير الخطابات. عند اكتمال الجزء سيظهر زر «طباعة الآن».',
  };
}

export default function FollowUpPrintJobDetailPage() {
  const { id } = useParams();
  const jobId = Number(id);
  const [job, setJob] = useState<FollowUpPrintJob | null>(null);
  const [loading, setLoading] = useState(true);
  const [acting, setActing] = useState(false);
  const [error, setError] = useState('');
  const [message, setMessage] = useState('');
  const pollSeq = useRef(0);
  // eslint-disable-next-line react-hooks/purity -- stable client observation timestamp for stale-job suppression.
  const pageOpenedAtRef = useRef(Date.now());
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

  const handleRefresh = () => { loadJob(() => true).catch(() => undefined); };

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

  const { variant: guidanceVariant, message: guidanceMessage } = getJobGuidanceAlert(job.status, job);

  return (
    <div dir="rtl">
      <PageHeader
        title={`مهمة الطباعة #${job.id}`}
        subtitle="تفاصيل المهمة وأجزاء الطباعة"
        actions={<Link to="/follow-up-print/jobs" className="btn btn-outline">العودة للمهام</Link>}
      />

      {message && <Alert variant="success">{message}</Alert>}
      {error && <Alert variant="error">{error}</Alert>}

      <Alert variant={guidanceVariant}>{guidanceMessage}</Alert>

      {/* eslint-disable-next-line react-hooks/refs -- read-only timestamp captured at mount for stale-job suppression. */}
      {isJobStale(job, pageOpenedAtRef.current) && (
        <Alert variant="error">
          يبدو أن تجهيز الخطابات متوقف أو لم يكتمل. يرجى إعادة محاولة إنشاء المهمة أو مراجعة سجل الأخطاء.
        </Alert>
      )}

      {(() => {
        const readyParts = job.parts.filter((p) => isPartPrintable(p));
        if (readyParts.length === 1) {
          return (
            <div className="mb-4">
              <Link
                to={`/follow-up-print/parts/${job.id}/${readyParts[0].partNumber}/print`}
                className="btn btn-primary btn-lg"
                target="_blank"
                rel="noreferrer"
              >
                طباعة الآن
              </Link>
            </div>
          );
        }
        return null;
      })()}

      <div className="card">
        <div className="details-banner-row">
          <span className={`badge ${followUpPrintJobStatusBadgeClass(job.status)}`}>
            {followUpPrintJobStatusLabels[job.status] ?? `حالة غير معروفة: ${job.status}`}
          </span>
          <span>المعاملات: {job.totalTransactions}</span>
          <span>الخطابات الجاهزة: {job.readyLetters}/{job.totalLetters}</span>
          <span>الأجزاء التي طُلبت طباعتها: {job.printedParts}/{job.totalParts}</span>
          <span>تاريخ الإنشاء: <DateDisplay date={job.createdAt} /></span>
          {job.startedAt && <span>بدأت المعالجة: <DateDisplay date={job.startedAt} /></span>}
          {job.failedAt && <span>تاريخ الفشل: <DateDisplay date={job.failedAt} /></span>}
        </div>
        {job.failureReason && <Alert variant="error">{job.failureReason}</Alert>}
        <div className="form-actions mt-4">
          <button type="button" className="btn btn-outline" disabled={loading} onClick={handleRefresh}>
            تحديث الحالة
          </button>
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
        <div className="card-header"><h3 className="card-title">أجزاء الطباعة</h3></div>
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
                    <span className={`badge ${getPartStatusBadgeClass(part.status)}`}>
                      {getPartStatusLabel(part.status)}
                    </span>
                  </td>
                  <td>{part.letterCount}</td>
                  <td>{part.estimatedPages}</td>
                  <td>{part.readyAt ? <DateDisplay date={part.readyAt} /> : '—'}</td>
                  <td>{part.printedAt ? <DateDisplay date={part.printedAt} /> : '—'}</td>
                  <td>{renderPartAction(job.id, part)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}
