import { useEffect, useState, useRef } from 'react';
import { departmentResponsesApi } from '../api/services';
import type { DepartmentResponseDto, DepartmentTransactionResponseItemDto } from '../api/types';
import { PageHeader, EmptyState, ErrorState } from '../components/ui';

const STATUS_LABELS: Record<string, string> = {
  Draft: 'مسودة',
  SubmittedForReview: 'مقدّم للمراجعة',
  ReturnedForCorrection: 'أُعيد للتصحيح',
  Approved: 'معتمد',
  Rejected: 'مرفوض',
};

const STATUS_BADGE: Record<string, string> = {
  Draft: 'badge-draft',
  SubmittedForReview: 'badge-submitted',
  ReturnedForCorrection: 'badge-returned',
  Approved: 'badge-approved',
  Rejected: 'badge-rejected',
};

function statusBadge(status: string) {
  const cls = STATUS_BADGE[status] ?? 'badge-draft';
  return (
    <span className={`inline-flex items-center px-2 py-0.5 rounded text-xs font-medium status-badge ${cls}`}>
      {STATUS_LABELS[status] ?? status}
    </span>
  );
}

type ViewState =
  | { kind: 'list' }
  | { kind: 'new'; transactionId: number; subject: string; trackingNumber: string }
  | { kind: 'detail'; id: number };

export default function DepartmentTransactionsPage() {
  const [transactions, setTransactions] = useState<DepartmentTransactionResponseItemDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [view, setView] = useState<ViewState>({ kind: 'list' });
  const [detail, setDetail] = useState<DepartmentResponseDto | null>(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [form, setForm] = useState({ responseText: '' });
  const [formError, setFormError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const fileInputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    loadList();
  }, []);

  async function loadList() {
    setLoading(true);
    setError(null);
    try {
      const res = await departmentResponsesApi.getDepartmentTransactions();
      setTransactions(res.data);
    } catch {
      setError('تعذر تحميل بيانات المعاملات');
    } finally {
      setLoading(false);
    }
  }

  async function openDetail(responseId: number) {
    setDetailLoading(true);
    setDetail(null);
    setForm({ responseText: '' });
    setFormError(null);
    setView({ kind: 'detail', id: responseId });
    try {
      const res = await departmentResponsesApi.getById(responseId);
      setDetail(res.data);
      setForm({ responseText: res.data.responseText });
    } catch {
      setError('تعذر تحميل تفاصيل الرد');
      setView({ kind: 'list' });
    } finally {
      setDetailLoading(false);
    }
  }

  function openCreate(tx: DepartmentTransactionResponseItemDto) {
    setFormError(null);
    setForm({ responseText: '' });
    setView({ kind: 'new', transactionId: tx.transactionId, subject: tx.subject, trackingNumber: tx.internalTrackingNumber });
  }

  async function handleCreate() {
    if (view.kind !== 'new') return;
    setFormError(null);
    if (!form.responseText.trim()) { setFormError('نص الرد مطلوب'); return; }
    setSaving(true);
    try {
      const res = await departmentResponsesApi.create({ transactionId: view.transactionId, responseText: form.responseText });
      await loadList();
      await openDetail(res.data.id);
    } catch (e: unknown) {
      const msg = (e as { response?: { data?: { message?: string } } })?.response?.data?.message;
      setFormError(msg ?? 'حدث خطأ أثناء الحفظ');
    } finally {
      setSaving(false);
    }
  }

  async function handleUpdate() {
    if (!detail) return;
    setFormError(null);
    if (!form.responseText.trim()) { setFormError('نص الرد مطلوب'); return; }
    setSaving(true);
    try {
      const res = await departmentResponsesApi.update(detail.id, { responseText: form.responseText });
      setDetail(res.data);
      setForm({ responseText: res.data.responseText });
    } catch (e: unknown) {
      const msg = (e as { response?: { data?: { message?: string } } })?.response?.data?.message;
      setFormError(msg ?? 'حدث خطأ أثناء الحفظ');
    } finally {
      setSaving(false);
    }
  }

  async function handleSubmit() {
    if (!detail) return;
    setSaving(true);
    try {
      const res = await departmentResponsesApi.submit(detail.id);
      setDetail(res.data);
      await loadList();
    } catch (e: unknown) {
      const msg = (e as { response?: { data?: { message?: string } } })?.response?.data?.message;
      setFormError(msg ?? 'حدث خطأ أثناء التقديم');
    } finally {
      setSaving(false);
    }
  }

  async function handleUpload(file: File) {
    if (!detail) return;
    try {
      const res = await departmentResponsesApi.uploadAttachment(detail.id, file);
      setDetail(d => d ? { ...d, attachments: [...d.attachments, res.data] } : d);
    } catch (e: unknown) {
      const msg = (e as { response?: { data?: { message?: string } } })?.response?.data?.message;
      setFormError(msg ?? 'فشل رفع المرفق');
    }
  }

  async function handleDeleteAttachment(attachmentId: number) {
    if (!detail) return;
    try {
      await departmentResponsesApi.deleteAttachment(detail.id, attachmentId);
      setDetail(d => d ? { ...d, attachments: d.attachments.filter(a => a.id !== attachmentId) } : d);
    } catch {
      setFormError('فشل حذف المرفق');
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

  function goBack() {
    setView({ kind: 'list' });
    loadList();
  }

  const isEditable = detail?.status === 'Draft' || detail?.status === 'ReturnedForCorrection';

  if (loading) {
    return (
      <div className="page-container" dir="rtl">
        <PageHeader title="معاملات إدارتي" />
        <div className="text-center py-12 text-gray-500">جارٍ التحميل...</div>
      </div>
    );
  }

  if (error && view.kind === 'list') {
    return (
      <div className="page-container" dir="rtl">
        <PageHeader title="معاملات إدارتي" />
        <ErrorState description={error ?? undefined} action={<button className="btn btn-secondary" onClick={loadList}>إعادة المحاولة</button>} />
      </div>
    );
  }

  if (view.kind === 'new') {
    return (
      <div className="page-container" dir="rtl">
        <PageHeader
          title={`إنشاء رد — ${view.trackingNumber}`}
          actions={
            <button className="btn btn-secondary" onClick={() => { setView({ kind: 'list' }); setFormError(null); }}>
              رجوع
            </button>
          }
        />
        <div className="card max-w-2xl">
          <div className="card-body space-y-4">
            {formError && <div className="alert alert-error">{formError}</div>}
            <div className="p-3 bg-gray-50 rounded text-sm">
              <span className="text-gray-500 ml-2">الموضوع:</span>
              <span className="font-medium">{view.subject}</span>
            </div>
            <div>
              <label className="form-label">نص الرد *</label>
              <textarea
                className="form-input"
                rows={6}
                value={form.responseText}
                onChange={e => setForm(f => ({ ...f, responseText: e.target.value }))}
                placeholder="اكتب رد إدارتك هنا..."
              />
            </div>
            <div className="flex gap-2">
              <button className="btn btn-primary" onClick={handleCreate} disabled={saving}>
                {saving ? 'جارٍ الحفظ...' : 'حفظ كمسودة'}
              </button>
            </div>
          </div>
        </div>
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

    return (
      <div className="page-container" dir="rtl">
        <PageHeader
          title={`رد إدارة — ${detail.internalTrackingNumber}`}
          actions={
            <button className="btn btn-secondary" onClick={goBack}>
              رجوع للقائمة
            </button>
          }
        />

        <div className="space-y-4 max-w-3xl">
          {formError && <div className="alert alert-error">{formError}</div>}

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
                {statusBadge(detail.status)}
              </div>
              {detail.reviewNote && (
                <div className="mt-2 p-3 bg-yellow-50 border border-yellow-200 rounded text-sm">
                  <strong>ملاحظة المراجع:</strong> {detail.reviewNote}
                </div>
              )}
            </div>
          </div>

          {isEditable ? (
            <div className="card">
              <div className="card-body space-y-3">
                <label className="form-label">نص الرد *</label>
                <textarea
                  className="form-input"
                  rows={7}
                  value={form.responseText}
                  onChange={e => setForm(f => ({ ...f, responseText: e.target.value }))}
                />
                <div className="flex gap-2">
                  <button className="btn btn-primary" onClick={handleUpdate} disabled={saving}>
                    {saving ? 'جارٍ الحفظ...' : 'حفظ التعديلات'}
                  </button>
                  <button className="btn btn-success" onClick={handleSubmit} disabled={saving}>
                    تقديم للمراجعة
                  </button>
                </div>
              </div>
            </div>
          ) : (
            <div className="card">
              <div className="card-body">
                <label className="form-label">نص الرد</label>
                <p className="whitespace-pre-wrap text-sm text-gray-700 mt-1">{detail.responseText}</p>
              </div>
            </div>
          )}

          <div className="card">
            <div className="card-body">
              <div className="flex justify-between items-center mb-3">
                <h3 className="font-medium">المرفقات ({detail.attachments.length})</h3>
                {isEditable && (
                  <>
                    <button className="btn btn-secondary btn-sm" onClick={() => fileInputRef.current?.click()}>
                      رفع مرفق
                    </button>
                    <input
                      ref={fileInputRef}
                      type="file"
                      className="hidden"
                      accept=".pdf,.jpg,.jpeg,.png,.docx"
                      onChange={e => e.target.files?.[0] && handleUpload(e.target.files[0])}
                    />
                  </>
                )}
              </div>
              {detail.attachments.length === 0 ? (
                <p className="text-sm text-gray-500">لا توجد مرفقات</p>
              ) : (
                <ul className="space-y-2">
                  {detail.attachments.map(a => (
                    <li key={a.id} className="flex items-center justify-between text-sm p-2 bg-gray-50 rounded">
                      <span>{a.originalFileName}</span>
                      <div className="flex gap-2">
                        <button
                          className="text-blue-600 hover:underline"
                          onClick={() => handleDownload(a.id, a.originalFileName)}
                        >
                          تحميل
                        </button>
                        {isEditable && (
                          <button
                            className="text-red-600 hover:underline"
                            onClick={() => handleDeleteAttachment(a.id)}
                          >
                            حذف
                          </button>
                        )}
                      </div>
                    </li>
                  ))}
                </ul>
              )}
            </div>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="page-container" dir="rtl">
      <PageHeader title="معاملات إدارتي" />

      {transactions.length === 0 ? (
        <EmptyState title="لا توجد معاملات مسندة لإدارتك حالياً" />
      ) : (
        <div className="overflow-x-auto">
          <table className="data-table w-full">
            <thead>
              <tr>
                <th>رقم المعاملة</th>
                <th>الموضوع</th>
                <th>حالة الإفادة</th>
                <th>تاريخ الإسناد</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {transactions.map(tx => (
                <tr key={tx.transactionId}>
                  <td>{tx.internalTrackingNumber}</td>
                  <td className="max-w-xs truncate">{tx.subject}</td>
                  <td>
                    {tx.departmentResponseStatus
                      ? statusBadge(tx.departmentResponseStatus)
                      : <span className="text-gray-400 text-xs">لم تُنشأ بعد</span>}
                  </td>
                  <td>{tx.assignedDate ? new Date(tx.assignedDate).toLocaleDateString('ar-SA') : '—'}</td>
                  <td>
                    {tx.canCreateResponse && (
                      <button className="text-green-600 hover:underline text-sm" onClick={() => openCreate(tx)}>
                        تسجيل إفادة
                      </button>
                    )}
                    {tx.canEditResponse && (
                      <button className="text-blue-600 hover:underline text-sm" onClick={() => openDetail(tx.departmentResponseId!)}>
                        تعديل الإفادة
                      </button>
                    )}
                    {tx.departmentResponseStatus === 'SubmittedForReview' && (
                      <button className="text-gray-400 text-sm cursor-not-allowed" disabled>
                        بانتظار المراجعة
                      </button>
                    )}
                    {tx.departmentResponseStatus === 'Approved' && (
                      <span className="text-green-600 text-xs font-medium">معتمدة</span>
                    )}
                    {tx.departmentResponseStatus === 'Rejected' && (
                      <span className="text-red-600 text-xs font-medium">مرفوضة</span>
                    )}
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
