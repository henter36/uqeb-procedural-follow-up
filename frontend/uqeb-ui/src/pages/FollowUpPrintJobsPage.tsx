import { useCallback, useState } from 'react';
import { Link } from 'react-router-dom';
import { followUpPrintApi } from '../api/services';
import type { FollowUpPrintJob, FollowUpPrintJobListStatusFilter } from '../api/types';
import { getApiErrorMessage } from '../utils/apiHelpers';
import {
  followUpPrintJobStatusBadgeClass,
  followUpPrintJobStatusLabels,
} from '../utils/followUpPrintLabels';
import DateDisplay from '../components/DateDisplay';
import {
  Alert, EmptyState, LoadingInline, PageHeader, Pagination,
} from '../components/ui';
import { useDeferredEffect } from '../hooks/useDeferredEffect';

export default function FollowUpPrintJobsPage() {
  const [jobs, setJobs] = useState<FollowUpPrintJob[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [status, setStatus] = useState<FollowUpPrintJobListStatusFilter>('Active');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  const loadJobs = useCallback(async (active: () => boolean) => {
    await Promise.resolve();
    if (active()) {
      setLoading(true);
      setError('');
    }
    try {
      const res = await followUpPrintApi.listJobs({ page, pageSize, status });
      if (!active()) return;
      setJobs(res.data.items);
      setTotalCount(res.data.totalCount);
    } catch (err: unknown) {
      if (!active()) return;
      setError(getApiErrorMessage(err));
    } finally {
      if (active()) setLoading(false);
    }
  }, [page, pageSize, status]);

  const findNextPrintablePart = (job: FollowUpPrintJob) =>
    job.parts.find((part) => ['ReadyToPrint', 'PartiallyReady'].includes(part.status));

  useDeferredEffect(loadJobs, [loadJobs]);

  const renderJobsContent = () => {
    if (loading) {
      return <LoadingInline label="جاري تحميل المهام..." />;
    }
    if (jobs.length === 0) {
      return <EmptyState title="لا توجد مهام طباعة" description="أنشئ مهمة من صفحة المعاملات المستحقة." />;
    }
    return (
      <>
        <div className="table-wrapper table-wrapper-spaced">
          <table className="data-table">
            <thead>
              <tr>
                <th>رقم المهمة</th>
                <th>الحالة</th>
                <th>المعاملات</th>
                <th>الخطابات</th>
                <th>الأجزاء</th>
                <th>تاريخ الإنشاء</th>
                <th>إجراءات</th>
              </tr>
            </thead>
            <tbody>
              {jobs.map((job) => (
                <tr key={job.id}>
                  <td>{job.id}</td>
                  <td>
                    <span className={`badge ${followUpPrintJobStatusBadgeClass(job.status)}`}>
                      {followUpPrintJobStatusLabels[job.status]}
                    </span>
                  </td>
                  <td>{job.totalTransactions}</td>
                  <td>{job.readyLetters}/{job.totalLetters}</td>
                  <td>{job.printedParts}/{job.totalParts}</td>
                  <td><DateDisplay date={job.createdAt} /></td>
                  <td className="btn-group">
                    {findNextPrintablePart(job) && (
                      <Link
                        to={`/follow-up-print/parts/${job.id}/${findNextPrintablePart(job)?.partNumber}/print`}
                        className="btn btn-sm btn-primary"
                        target="_blank"
                        rel="noreferrer"
                      >
                        طباعة الجزء التالي
                      </Link>
                    )}
                    <Link to={`/follow-up-print/jobs/${job.id}`} className="btn btn-sm btn-outline">تفاصيل</Link>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <Pagination
          page={page}
          pageSize={pageSize}
          total={totalCount}
          itemCount={jobs.length}
          onPageChange={setPage}
          onPageSizeChange={(size) => { setPageSize(size); setPage(1); }}
        />
      </>
    );
  };

  return (
    <div dir="rtl">
      <PageHeader
        title="مهام طباعة التعقيب"
        actions={(
          <>
            <Link to="/follow-up-print/eligible" className="btn btn-outline">المعاملات المستحقة</Link>
            <Link to="/follow-up-print/pending" className="btn btn-outline">بانتظار تسجيل التعقيب</Link>
          </>
        )}
      />

      {error && <Alert variant="error">{error}</Alert>}
      <Alert variant="info">
        تقوم المهمة بتحضير الخطابات فقط. لا تتم الطباعة تلقائيًا، ويجب فتح الجزء الجاهز والضغط على طباعة.
      </Alert>

      <div className="card">
        <div className="form-actions mb-3">
          <label htmlFor="job-status-filter">فلتر الحالة</label>
          <select
            id="job-status-filter"
            value={status}
            onChange={(event) => {
              setStatus(event.target.value as FollowUpPrintJobListStatusFilter);
              setPage(1);
            }}
          >
            <option value="Active">النشطة</option>
            <option value="ReadyToPrint">الجاهزة للطباعة</option>
            <option value="Completed">المكتملة</option>
            <option value="Failed">الفاشلة</option>
            <option value="Cancelled">الملغاة</option>
            <option value="All">الكل</option>
          </select>
        </div>
        {renderJobsContent()}
      </div>
    </div>
  );
}
