import { useCallback, useState } from 'react';
import { Link } from 'react-router-dom';
import { followUpPrintApi } from '../api/services';
import type { FollowUpLetterPrintRecord } from '../api/types';
import { getApiErrorMessage } from '../utils/apiHelpers';
import DateDisplay from '../components/DateDisplay';
import {
  Alert, EmptyState, LoadingInline, PageHeader, Pagination,
} from '../components/ui';
import { useDeferredEffect } from '../hooks/useDeferredEffect';

export default function FollowUpPrintPendingPage() {
  const [records, setRecords] = useState<FollowUpLetterPrintRecord[]>([]);
  const [summaryTotal, setSummaryTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [loading, setLoading] = useState(true);
  const [actingId, setActingId] = useState<number | null>(null);
  const [error, setError] = useState('');
  const [message, setMessage] = useState('');

  const loadRecords = useCallback(async (active: () => boolean) => {
    await Promise.resolve();
    if (active()) {
      setLoading(true);
      setError('');
    }
    try {
      const [summaryRes, recordsRes] = await Promise.all([
        followUpPrintApi.getPendingSummary(),
        followUpPrintApi.getPending({ page, pageSize }),
      ]);
      if (!active()) return;
      setSummaryTotal(summaryRes.data.total);
      setRecords(recordsRes.data);
    } catch (err: unknown) {
      if (!active()) return;
      setError(getApiErrorMessage(err));
    } finally {
      if (active()) setLoading(false);
    }
  }, [page, pageSize]);

  useDeferredEffect(loadRecords, [loadRecords]);

  const refreshRecords = useCallback(async () => {
    await loadRecords(() => true);
  }, [loadRecords]);

  const handleConfirm = async (record: FollowUpLetterPrintRecord) => {
    setActingId(record.id);
    setError('');
    try {
      await followUpPrintApi.confirmRecord(record.id);
      setMessage(`تم تأكيد طباعة ${record.incomingNumber}.`);
      await refreshRecords();
    } catch (err: unknown) {
      setError(getApiErrorMessage(err));
    } finally {
      setActingId(null);
    }
  };

  const handleCancel = async (record: FollowUpLetterPrintRecord) => {
    const reason = globalThis.prompt('سبب الإلغاء:');
    if (!reason?.trim()) return;
    setActingId(record.id);
    setError('');
    try {
      await followUpPrintApi.cancelRecord(record.id, reason.trim());
      setMessage(`تم إلغاء سجل ${record.incomingNumber}.`);
      await refreshRecords();
    } catch (err: unknown) {
      setError(getApiErrorMessage(err));
    } finally {
      setActingId(null);
    }
  };

  const handleReprint = async (record: FollowUpLetterPrintRecord) => {
    setActingId(record.id);
    setError('');
    try {
      await followUpPrintApi.reprintRecord(record.id);
      setMessage(`تم إنشاء إعادة طباعة لـ ${record.incomingNumber}.`);
      await refreshRecords();
    } catch (err: unknown) {
      setError(getApiErrorMessage(err));
    } finally {
      setActingId(null);
    }
  };

  const handleLinkFollowUp = async (record: FollowUpLetterPrintRecord) => {
    const followUpIdRaw = globalThis.prompt('رقم التعقيب المسجل:');
    const followUpId = Number(followUpIdRaw);
    if (!Number.isFinite(followUpId) || followUpId <= 0) return;
    setActingId(record.id);
    setError('');
    try {
      await followUpPrintApi.linkFollowUp(record.id, followUpId);
      setMessage(`تم ربط ${record.incomingNumber} بالتعقيب ${followUpId}.`);
      await refreshRecords();
    } catch (err: unknown) {
      setError(getApiErrorMessage(err));
    } finally {
      setActingId(null);
    }
  };

  const renderRecordsContent = () => {
    if (loading) {
      return <LoadingInline label="جاري التحميل..." />;
    }
    if (records.length === 0) {
      return <EmptyState title="لا توجد سجلات معلقة" description="جميع الخطابات المطبوعة مسجلة." />;
    }
    return (
      <>
        <div className="table-wrapper table-wrapper-spaced">
          <table className="data-table">
            <thead>
              <tr>
                <th>رقم الوارد</th>
                <th>الموضوع</th>
                <th>الجهة</th>
                <th>ترتيب التعقيب</th>
                <th>تاريخ الطلب</th>
                <th>إجراءات</th>
              </tr>
            </thead>
            <tbody>
              {records.map((record) => (
                <tr key={record.id}>
                  <td>{record.incomingNumber}</td>
                  <td>{record.subject}</td>
                  <td>{record.targetEntityNameSnapshot ?? '—'}</td>
                  <td>{record.followUpSequence}</td>
                  <td><DateDisplay date={record.printRequestedAt} /></td>
                  <td className="btn-group">
                    <Link to={`/transactions/${record.transactionId}`} className="btn btn-sm btn-outline">المعاملة</Link>
                    <button
                      type="button"
                      className="btn btn-sm btn-primary"
                      disabled={actingId === record.id}
                      onClick={() => { handleConfirm(record).catch(() => undefined); }}
                    >
                      تأكيد
                    </button>
                    <button
                      type="button"
                      className="btn btn-sm btn-secondary"
                      disabled={actingId === record.id}
                      onClick={() => { handleLinkFollowUp(record).catch(() => undefined); }}
                    >
                      ربط تعقيب
                    </button>
                    <button
                      type="button"
                      className="btn btn-sm btn-outline"
                      disabled={actingId === record.id}
                      onClick={() => { handleReprint(record).catch(() => undefined); }}
                    >
                      إعادة طباعة
                    </button>
                    <button
                      type="button"
                      className="btn btn-sm btn-outline"
                      disabled={actingId === record.id}
                      onClick={() => { handleCancel(record).catch(() => undefined); }}
                    >
                      إلغاء
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        <Pagination
          page={page}
          pageSize={pageSize}
          total={summaryTotal}
          itemCount={records.length}
          onPageChange={setPage}
          onPageSizeChange={(size) => { setPageSize(size); setPage(1); }}
        />
      </>
    );
  };

  return (
    <div dir="rtl">
      <PageHeader
        title="خطابات بانتظار التسجيل"
        subtitle="سجلات طُلبت طباعتها ولم تُسجّل بعد كتعقيب"
        actions={(
          <>
            <Link to="/follow-up-print/jobs" className="btn btn-outline">مهام الطباعة</Link>
            <Link to="/follow-up-print/eligible" className="btn btn-outline">المعاملات المستحقة</Link>
          </>
        )}
      />

      {message && <Alert variant="success">{message}</Alert>}
      {error && <Alert variant="error">{error}</Alert>}

      <div className="card mb-4">
        <strong>إجمالي المعلق:</strong> {summaryTotal}
      </div>

      <div className="card">
        {renderRecordsContent()}
      </div>
    </div>
  );
}
