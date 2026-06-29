import { useCallback, useEffect, useRef, useState } from 'react';
import { useSearchParams } from 'react-router-dom';
import { departmentResponsesApi } from '../api/services';
import type { DepartmentResponseDto, DepartmentTransactionResponseItemDto, DepartmentResponseStatsDto } from '../api/types';
import { EmptyState, ErrorState, PageHeader } from '../components/ui';

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

// ─── EmployeeStatsBanner ──────────────────────────────────────────────────────

type StatsBannerState =
  | { kind: 'loading' }
  | { kind: 'error'; message: string }
  | { kind: 'ready'; stats: DepartmentResponseStatsDto };

type StatCardProps = Readonly<{
  label: string;
  value: number;
  tone?: 'default' | 'red' | 'gold' | 'green';
}>;

type StatCardTone = NonNullable<StatCardProps['tone']>;

function StatCard({ label, value, tone = 'default' }: StatCardProps) {
  const toneClass: Record<StatCardTone, string> = {
    default: 'dept-stat-card',
    red: 'dept-stat-card dept-stat-card--red',
    gold: 'dept-stat-card dept-stat-card--gold',
    green: 'dept-stat-card dept-stat-card--green',
  };
  return (
    <div className={toneClass[tone]}>
      <span className="dept-stat-value">{value}</span>
      <span className="dept-stat-label">{label}</span>
    </div>
  );
}

function EmployeeStatsBanner({ state }: Readonly<{ state: StatsBannerState }>) {
  if (state.kind === 'loading') {
    return (
      <div className="dept-stats-banner dept-stats-banner--loading" role="status" aria-live="polite">
        جارٍ تحميل الإحصائيات...
      </div>
    );
  }

  if (state.kind === 'error') {
    return (
      <div className="dept-stats-banner dept-stats-banner--error" role="alert">
        {state.message}
      </div>
    );
  }

  const { stats } = state;

  if (stats.totalAssigned === 0) {
    return (
      <div className="dept-stats-banner dept-stats-banner--empty">
        لا توجد إفادات مطلوبة حاليًا.
      </div>
    );
  }

  return (
    <section className="dept-stats-grid" aria-label="إحصائيات إفادات إدارتي">
      <StatCard label="إجمالي المُسندة" value={stats.totalAssigned} />
      <StatCard label="لم تُنشأ بعد" value={stats.pendingResponse} tone={stats.pendingResponse > 0 ? 'gold' : 'default'} />
      <StatCard label="مسودة" value={stats.draft} />
      <StatCard label="بانتظار المراجعة" value={stats.submittedForReview} tone="gold" />
      <StatCard label="أُعيدت للتصحيح" value={stats.returnedForCorrection} tone={stats.returnedForCorrection > 0 ? 'red' : 'default'} />
      <StatCard label="معتمدة" value={stats.approved} tone="green" />
      <StatCard label="مرفوضة" value={stats.rejected} tone={stats.rejected > 0 ? 'red' : 'default'} />
    </section>
  );
}

// ─── LoadingView ──────────────────────────────────────────────────────────────

function LoadingView() {
  return (
    <div className="page-container" dir="rtl">
      <PageHeader title="معاملات إدارتي" />
      <div className="text-center py-12 text-gray-500">جارٍ التحميل...</div>
    </div>
  );
}

// ─── ListErrorView ────────────────────────────────────────────────────────────

type ListErrorViewProps = Readonly<{
  error: string;
  onRetry: () => void;
}>;

function ListErrorView({ error, onRetry }: ListErrorViewProps) {
  return (
    <div className="page-container" dir="rtl">
      <PageHeader title="معاملات إدارتي" />
      <ErrorState
        description={error}
        action={<button className="btn btn-secondary" onClick={onRetry}>إعادة المحاولة</button>}
      />
    </div>
  );
}

// ─── TransactionActionCell ────────────────────────────────────────────────────

type TransactionActionCellProps = Readonly<{
  tx: DepartmentTransactionResponseItemDto;
  onOpenCreate: () => void;
  onOpenDetail: () => void;
}>;

function TransactionActionCell({ tx, onOpenCreate, onOpenDetail }: TransactionActionCellProps) {
  if (tx.canCreateResponse) {
    return <button className="text-green-600 hover:underline text-sm" onClick={onOpenCreate}>تسجيل إفادة</button>;
  }
  if (tx.canEditResponse) {
    return <button className="text-blue-600 hover:underline text-sm" onClick={onOpenDetail}>تعديل الإفادة</button>;
  }
  if (tx.departmentResponseStatus === 'SubmittedForReview') {
    return <button className="text-gray-400 text-sm cursor-not-allowed" disabled>بانتظار المراجعة</button>;
  }
  if (tx.departmentResponseStatus === 'Approved') {
    return <span className="text-green-600 text-xs font-medium">معتمدة</span>;
  }
  if (tx.departmentResponseStatus === 'Rejected') {
    return <span className="text-red-600 text-xs font-medium">مرفوضة</span>;
  }
  return null;
}

// ─── TransactionsListView ─────────────────────────────────────────────────────

type TransactionsListViewProps = Readonly<{
  transactions: DepartmentTransactionResponseItemDto[];
  statsState: StatsBannerState;
  onOpenCreate: (tx: DepartmentTransactionResponseItemDto) => void;
  onOpenDetail: (id: number) => void;
}>;

function TransactionsListView({ transactions, statsState, onOpenCreate, onOpenDetail }: TransactionsListViewProps) {
  return (
    <div className="page-container" dir="rtl">
      <PageHeader title="معاملات إدارتي" />
      <EmployeeStatsBanner state={statsState} />
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
                    <TransactionActionCell
                      tx={tx}
                      onOpenCreate={() => onOpenCreate(tx)}
                      onOpenDetail={() => onOpenDetail(tx.departmentResponseId!)}
                    />
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

// ─── NewResponseView ──────────────────────────────────────────────────────────

type NewResponseViewProps = Readonly<{
  trackingNumber: string;
  subject: string;
  form: Readonly<{ responseText: string }>;
  formError: string | null;
  saving: boolean;
  onBack: () => void;
  onFormChange: (text: string) => void;
  onSave: () => void;
}>;

function NewResponseView({
  trackingNumber, subject, form, formError, saving, onBack, onFormChange, onSave,
}: NewResponseViewProps) {
  return (
    <div className="page-container" dir="rtl">
      <PageHeader
        title={`إنشاء رد — ${trackingNumber}`}
        actions={
          <button className="btn btn-secondary" onClick={onBack}>
            رجوع
          </button>
        }
      />
      <div className="card max-w-2xl">
        <div className="card-body space-y-4">
          {formError && <div className="alert alert-error">{formError}</div>}
          <div className="p-3 bg-gray-50 rounded text-sm">
            <span className="text-gray-500 ml-2">الموضوع:</span>
            <span className="font-medium">{subject}</span>
          </div>
          <div>
            <label className="form-label" htmlFor="new-response-text">نص الرد *</label>
            <textarea
              id="new-response-text"
              className="form-input"
              rows={6}
              value={form.responseText}
              onChange={e => onFormChange(e.target.value)}
              placeholder="اكتب رد إدارتك هنا..."
            />
          </div>
          <div className="flex gap-2">
            <button className="btn btn-primary" onClick={onSave} disabled={saving}>
              {saving ? 'جارٍ الحفظ...' : 'حفظ كمسودة'}
            </button>
          </div>
        </div>
      </div>
    </div>
  );
}

// ─── DetailResponseView ───────────────────────────────────────────────────────

type DetailResponseViewProps = Readonly<{
  detail: DepartmentResponseDto | null;
  detailLoading: boolean;
  form: Readonly<{ responseText: string }>;
  formError: string | null;
  saving: boolean;
  onBack: () => void;
  onFormChange: (text: string) => void;
  onUpdate: () => void;
  onSubmit: () => void;
  onUpload: (file: File) => void;
  onDeleteAttachment: (id: number) => void;
  onDownload: (id: number, name: string) => void;
}>;

function DetailResponseView({
  detail, detailLoading, form, formError, saving,
  onBack, onFormChange, onUpdate, onSubmit, onUpload, onDeleteAttachment, onDownload,
}: DetailResponseViewProps) {
  const fileInputRef = useRef<HTMLInputElement>(null);

  if (detailLoading || !detail) {
    return (
      <div className="page-container" dir="rtl">
        <div className="text-center py-12 text-gray-500">جارٍ التحميل...</div>
      </div>
    );
  }

  const isEditable = detail.status === 'Draft' || detail.status === 'ReturnedForCorrection';

  return (
    <div className="page-container" dir="rtl">
      <PageHeader
        title={`رد إدارة — ${detail.internalTrackingNumber}`}
        actions={
          <button className="btn btn-secondary" onClick={onBack}>
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
              <label className="form-label" htmlFor="edit-response-text">نص الرد *</label>
              <textarea
                id="edit-response-text"
                className="form-input"
                rows={7}
                value={form.responseText}
                onChange={e => onFormChange(e.target.value)}
              />
              <div className="flex gap-2">
                <button className="btn btn-primary" onClick={onUpdate} disabled={saving}>
                  {saving ? 'جارٍ الحفظ...' : 'حفظ التعديلات'}
                </button>
                <button className="btn btn-success" onClick={onSubmit} disabled={saving}>
                  تقديم للمراجعة
                </button>
              </div>
            </div>
          </div>
        ) : (
          <div className="card">
            <div className="card-body">
              <div className="form-label">نص الرد</div>
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
                    onChange={e => e.target.files?.[0] && onUpload(e.target.files[0])}
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
                        onClick={() => onDownload(a.id, a.originalFileName)}
                      >
                        تحميل
                      </button>
                      {isEditable && (
                        <button
                          className="text-red-600 hover:underline"
                          onClick={() => onDeleteAttachment(a.id)}
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

// ─── Page ─────────────────────────────────────────────────────────────────────

export default function DepartmentTransactionsPage() {
  const [searchParams] = useSearchParams();
  const requestedTransactionId = Number(searchParams.get('transactionId'));
  const [transactions, setTransactions] = useState<DepartmentTransactionResponseItemDto[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [view, setView] = useState<ViewState>({ kind: 'list' });
  const [detail, setDetail] = useState<DepartmentResponseDto | null>(null);
  const [detailLoading, setDetailLoading] = useState(false);
  const [form, setForm] = useState({ responseText: '' });
  const [formError, setFormError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [statsState, setStatsState] = useState<StatsBannerState>({ kind: 'loading' });

  const openRequestedTransaction = useCallback((items: DepartmentTransactionResponseItemDto[]) => {
    if (!Number.isFinite(requestedTransactionId) || requestedTransactionId <= 0) return;

    const tx = items.find(item => item.transactionId === requestedTransactionId);
    if (!tx) return;

    if (tx.canCreateResponse) {
      openCreate(tx);
    } else if (tx.departmentResponseId) {
      openDetail(tx.departmentResponseId).catch(() => undefined);
    }
  }, [requestedTransactionId]);

  const loadStats = useCallback(async () => {
    setStatsState({ kind: 'loading' });
    try {
      const res = await departmentResponsesApi.getMyStats();
      setStatsState({ kind: 'ready', stats: res.data });
    } catch (err: unknown) {
      const status = (err as { response?: { status?: number } })?.response?.status;
      if (status === 403) {
        setStatsState({ kind: 'error', message: 'ليست لديك صلاحية لعرض هذه البيانات.' });
      } else {
        setStatsState({ kind: 'error', message: 'تعذر تحميل إحصائيات الموظف.' });
      }
    }
  }, []);

  const loadList = useCallback(async (background = false) => {
    if (!background) setLoading(true);
    setError(null);
    try {
      const res = await departmentResponsesApi.getDepartmentTransactions();
      setTransactions(res.data);
      openRequestedTransaction(res.data);
      return res.data;
    } catch {
      setError('تعذر تحميل بيانات المعاملات');
      return [];
    } finally {
      if (!background) setLoading(false);
    }
  }, [openRequestedTransaction]);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    loadStats();
    loadList().catch(() => undefined);
  }, [loadList, loadStats]);

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
      await Promise.all([loadList(true), loadStats()]);
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
      await Promise.all([loadList(true), loadStats()]);
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

  if (loading) return <LoadingView />;
  if (error && view.kind === 'list') return <ListErrorView error={error} onRetry={loadList} />;
  if (view.kind === 'new') return (
    <NewResponseView
      trackingNumber={view.trackingNumber}
      subject={view.subject}
      form={form}
      formError={formError}
      saving={saving}
      onBack={() => { setView({ kind: 'list' }); setFormError(null); }}
      onFormChange={text => setForm(f => ({ ...f, responseText: text }))}
      onSave={handleCreate}
    />
  );
  if (view.kind === 'detail') return (
    <DetailResponseView
      detail={detail}
      detailLoading={detailLoading}
      form={form}
      formError={formError}
      saving={saving}
      onBack={goBack}
      onFormChange={text => setForm(f => ({ ...f, responseText: text }))}
      onUpdate={handleUpdate}
      onSubmit={handleSubmit}
      onUpload={handleUpload}
      onDeleteAttachment={handleDeleteAttachment}
      onDownload={handleDownload}
    />
  );
  return (
    <TransactionsListView
      transactions={transactions}
      statsState={statsState}
      onOpenCreate={openCreate}
      onOpenDetail={openDetail}
    />
  );
}
