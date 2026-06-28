import { useEffect, useState } from 'react';
import { departmentResponsesApi } from '../api/services';
import type { DepartmentResponseDto, DepartmentResponseSummaryDto } from '../api/types';
import { PageHeader, EmptyState, ErrorState } from '../components/ui';

const STATUS_LABELS: Record<string, string> = {
  Draft: 'مسودة',
  SubmittedForReview: 'مقدّم للمراجعة',
  ReturnedForCorrection: 'أُعيد للتصحيح',
  Approved: 'معتمد',
  Rejected: 'مرفوض',
};

type ViewState = { kind: 'list' } | { kind: 'detail'; id: number };

export default function DepartmentResponseReviewPage() {
  const [pending, setPending] = useState<DepartmentResponseSummaryDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [view, setView] = useState<ViewState>({ kind: 'list' });
  const [detail, setDetail] = useState<DepartmentResponseDto | null>(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [reviewNote, setReviewNote] = useState('');
  const [actionError, setActionError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    loadList();
  }, []);

  async function loadList() {
    setLoading(true);
    setError(null);
    try {
      const res = await departmentResponsesApi.getPendingReview();
      setPending(res.data);
    } catch {
      setError('تعذر تحميل البيانات');
    } finally {
      setLoading(false);
    }
  }

  async function openDetail(id: number) {
    setDetailLoading(true);
    setDetail(null);
    setReviewNote('');
    setActionError(null);
    setView({ kind: 'detail', id });
    try {
      const res = await departmentResponsesApi.getById(id);
      setDetail(res.data);
    } catch {
      setError('تعذر تحميل تفاصيل الرد');
    } finally {
      setDetailLoading(false);
    }
  }

  async function handleAction(action: 'approve' | 'return' | 'reject') {
    if (!detail) return;
    if ((action === 'return' || action === 'reject') && !reviewNote.trim()) {
      setActionError('الملاحظة مطلوبة عند الإعادة أو الرفض');
      return;
    }
    setSaving(true);
    setActionError(null);
    try {
      let res;
      if (action === 'approve') res = await departmentResponsesApi.approve(detail.id);
      else if (action === 'return') res = await departmentResponsesApi.returnForCorrection(detail.id, reviewNote);
      else res = await departmentResponsesApi.reject(detail.id, reviewNote);
      setDetail(res.data);
      await loadList();
    } catch (e: unknown) {
      const msg = (e as { response?: { data?: { message?: string } } })?.response?.data?.message;
      setActionError(msg ?? 'حدث خطأ أثناء تنفيذ الإجراء');
    } finally {
      setSaving(false);
    }
  }

  async function handleDownload(attachmentId: number, fileName: string) {
    if (!detail) return;
    const res = await departmentResponsesApi.downloadAttachment(detail.id, attachmentId);
    const url = URL.createObjectURL(new Blob([res.data]));
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    a.click();
    URL.revokeObjectURL(url);
  }

  if (loading) {
    return (
      <div className="page-container" dir="rtl">
        <PageHeader title="إفادات بانتظار المراجعة" />
        <div className="text-center py-12 text-gray-500">جارٍ التحميل...</div>
      </div>
    );
  }

  if (error && view.kind === 'list') {
    return (
      <div className="page-container" dir="rtl">
        <PageHeader title="إفادات بانتظار المراجعة" />
        <ErrorState message={error} onRetry={loadList} />
      </div>
    );
  }

  if (view.kind === 'detail') {
    if (detailLoading || !detail) {
      return (
        <div className="page-container" dir="rtl">
          <div className="text-center py-12 text-gray-500">جارٍ التحميل...</div>
        </div>
      );
    }

    const isPendingReview = detail.status === 'SubmittedForReview';

    return (
      <div className="page-container" dir="rtl">
        <PageHeader
          title={`مراجعة إفادة — معاملة ${detail.internalTrackingNumber}`}
          actions={
            <button className="btn btn-secondary" onClick={() => setView({ kind: 'list' })}>
              رجوع للقائمة
            </button>
          }
        />

        <div className="space-y-4 max-w-3xl">
          {actionError && <div className="alert alert-error">{actionError}</div>}

          <div className="card">
            <div className="card-body space-y-2 text-sm">
              <div className="flex justify-between">
                <span className="text-gray-500">الموضوع:</span>
                <span className="font-medium">{detail.transactionSubject}</span>
              </div>
              <div className="flex justify-between">
                <span className="text-gray-500">الإدارة:</span>
                <span>{detail.departmentName}</span>
              </div>
              <div className="flex justify-between">
                <span className="text-gray-500">الحالة:</span>
                <span className="font-medium">{STATUS_LABELS[detail.status] ?? detail.status}</span>
              </div>
              <div className="flex justify-between">
                <span className="text-gray-500">مقدَّم بواسطة:</span>
                <span>{detail.submittedByName}</span>
              </div>
              {detail.submittedAt && (
                <div className="flex justify-between">
                  <span className="text-gray-500">تاريخ التقديم:</span>
                  <span>{new Date(detail.submittedAt).toLocaleString('ar-SA')}</span>
                </div>
              )}
            </div>
          </div>

          <div className="card">
            <div className="card-body">
              <label className="form-label">نص الرد</label>
              <p className="whitespace-pre-wrap text-sm text-gray-700 mt-1 p-3 bg-gray-50 rounded">
                {detail.responseText}
              </p>
            </div>
          </div>

          {detail.attachments.length > 0 && (
            <div className="card">
              <div className="card-body">
                <h3 className="font-medium mb-3">المرفقات ({detail.attachments.length})</h3>
                <ul className="space-y-2">
                  {detail.attachments.map(a => (
                    <li key={a.id} className="flex items-center justify-between text-sm p-2 bg-gray-50 rounded">
                      <span>{a.originalFileName}</span>
                      <button
                        className="text-blue-600 hover:underline"
                        onClick={() => handleDownload(a.id, a.originalFileName)}
                      >
                        تحميل
                      </button>
                    </li>
                  ))}
                </ul>
              </div>
            </div>
          )}

          {isPendingReview && (
            <div className="card">
              <div className="card-body space-y-3">
                <label className="form-label">ملاحظة المراجع (مطلوبة عند الإعادة أو الرفض)</label>
                <textarea
                  className="form-input"
                  rows={4}
                  value={reviewNote}
                  onChange={e => setReviewNote(e.target.value)}
                  placeholder="اكتب ملاحظتك هنا..."
                />
                <div className="flex gap-3">
                  <button
                    className="btn btn-success"
                    onClick={() => handleAction('approve')}
                    disabled={saving}
                  >
                    {saving ? '...' : 'قبول'}
                  </button>
                  <button
                    className="btn btn-warning"
                    onClick={() => handleAction('return')}
                    disabled={saving}
                  >
                    إعادة للتصحيح
                  </button>
                  <button
                    className="btn btn-danger"
                    onClick={() => handleAction('reject')}
                    disabled={saving}
                  >
                    رفض
                  </button>
                </div>
              </div>
            </div>
          )}

          {!isPendingReview && detail.reviewedByName && (
            <div className="card">
              <div className="card-body text-sm space-y-1">
                <div className="flex justify-between">
                  <span className="text-gray-500">القرار:</span>
                  <span className="font-medium">{STATUS_LABELS[detail.status] ?? detail.status}</span>
                </div>
                <div className="flex justify-between">
                  <span className="text-gray-500">بواسطة:</span>
                  <span>{detail.reviewedByName}</span>
                </div>
                {detail.reviewNote && (
                  <div className="mt-2 p-3 bg-yellow-50 border border-yellow-200 rounded">
                    <strong>الملاحظة:</strong> {detail.reviewNote}
                  </div>
                )}
              </div>
            </div>
          )}
        </div>
      </div>
    );
  }

  return (
    <div className="page-container" dir="rtl">
      <PageHeader title={`إفادات بانتظار المراجعة (${pending.length})`} />

      {pending.length === 0 ? (
        <EmptyState title="لا توجد إفادات بانتظار المراجعة حالياً" />
      ) : (
        <div className="overflow-x-auto">
          <table className="data-table w-full">
            <thead>
              <tr>
                <th>رقم المعاملة</th>
                <th>الموضوع</th>
                <th>الإدارة</th>
                <th>تاريخ التقديم</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {pending.map(r => (
                <tr key={r.id}>
                  <td>{r.internalTrackingNumber}</td>
                  <td className="max-w-xs truncate">{r.transactionSubject}</td>
                  <td>{r.departmentName}</td>
                  <td>{r.submittedAt ? new Date(r.submittedAt).toLocaleString('ar-SA') : '—'}</td>
                  <td>
                    <button className="text-blue-600 hover:underline text-sm" onClick={() => openDetail(r.id)}>
                      مراجعة
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
