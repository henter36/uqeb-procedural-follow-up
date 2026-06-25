import { useCallback, useState } from 'react';
import { Link } from 'react-router-dom';
import { followUpPrintApi } from '../api/services';
import type { FollowUpPrintJob } from '../api/types';
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
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  const loadJobs = useCallback(async (active: () => boolean) => {
    await Promise.resolve();
    if (active()) {
      setLoading(true);
      setError('');
    }
    try {
      const res = await followUpPrintApi.listJobs({ page, pageSize });
      if (!active()) return;
      setJobs(res.data.items);
      setTotalCount(res.data.totalCount);
    } catch (err: unknown) {
      if (!active()) return;
      setError(getApiErrorMessage(err));
    } finally {
      if (active()) setLoading(false);
    }
  }, [page, pageSize]);

  useDeferredEffect(loadJobs, [loadJobs]);

  return (
    <div dir="rtl">
      <PageHeader
        title="مهام طباعة التعقيب"
        subtitle="متابعة مهام الطباعة الجماعية وحالة الأجزاء"
        actions={(
          <>
            <Link to="/follow-up-print/eligible" className="btn btn-outline">المعاملات المستحقة</Link>
            <Link to="/follow-up-print/pending" className="btn btn-outline">بانتظار التسجيل</Link>
          </>
        )}
      />

      {error && <Alert variant="error">{error}</Alert>}

      <div className="card">
        {loading ? (
          <LoadingInline label="جاري تحميل المهام..." />
        ) : jobs.length === 0 ? (
          <EmptyState title="لا توجد مهام طباعة" description="أنشئ مهمة من صفحة المعاملات المستحقة." />
        ) : (
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
                    <th>عرض</th>
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
                      <td><Link to={`/follow-up-print/jobs/${job.id}`} className="btn btn-sm btn-outline">تفاصيل</Link></td>
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
        )}
      </div>
    </div>
  );
}
