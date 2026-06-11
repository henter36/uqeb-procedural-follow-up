import { useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import { useParams, Link, useNavigate } from 'react-router-dom';
import { transactionsApi, departmentsApi } from '../api/services';
import type { TransactionDetail, Department, FollowUpDepartmentOption } from '../api/types';
import { useAuth } from '../context/AuthContext';
import { statusLabels, priorityLabels, statusBadgeClass, responseTypeLabels, auditActionLabels } from '../utils/labels';
import {
  buildCompleteResponsePayload,
  buildCreateAssignmentPayload,
  buildCreateFollowUpPayload,
  buildReplyPayload,
  getApiErrorMessage,
} from '../utils/apiHelpers';
import DateDisplay from '../components/DateDisplay';
import MultiSelect from '../components/MultiSelect';

export default function TransactionDetailPage() {
  const { id } = useParams();
  const navigate = useNavigate();
  const { canEdit, canClose, isDepartmentUser } = useAuth();
  const [tx, setTx] = useState<TransactionDetail | null>(null);
  const [departments, setDepartments] = useState<Department[]>([]);
  const [showFollowUp, setShowFollowUp] = useState(false);
  const [showAssignment, setShowAssignment] = useState(false);
  const [replyAssignmentId, setReplyAssignmentId] = useState<number | null>(null);
  const [showCompleteResponse, setShowCompleteResponse] = useState(false);
  const [message, setMessage] = useState('');
  const [error, setError] = useState('');

  const load = () => {
    if (!id) return;
    transactionsApi.getById(+id).then((r) => setTx(r.data)).catch(() => navigate('/transactions'));
  };

  useEffect(() => {
    load();
    departmentsApi.getAll().then((r) => setDepartments(r.data));
  }, [id]);

  const handleClose = async () => {
    if (!tx) return;
    const needsResponse = tx.requiresResponse || tx.responseType !== 'None';
    if (needsResponse && !tx.responseCompleted) {
      setError('لا يمكن إغلاق المعاملة قبل تسجيل الإفادة.');
      return;
    }
    if (!confirm('هل تريد إغلاق المعاملة؟')) return;
    setError('');
    try {
      await transactionsApi.close(+id!);
      load();
      setMessage('تم إغلاق المعاملة');
    } catch (err: unknown) {
      setError(getApiErrorMessage(err));
    }
  };

  const handleFileUpload = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;
    await transactionsApi.uploadAttachment(+id!, file);
    load();
  };

  const downloadAttachment = async (attachmentId: number, fileName: string) => {
    const res = await transactionsApi.downloadAttachment(+id!, attachmentId);
    const url = window.URL.createObjectURL(res.data);
    const a = document.createElement('a');
    a.href = url; a.download = fileName; a.click();
  };

  if (!tx) return <div className="loading">جاري التحميل...</div>;

  const needsResponse = tx.requiresResponse || tx.responseType !== 'None';
  const isTerminal = tx.status === 'Closed' || tx.status === 'Cancelled' || tx.status === 'Archived';
  const hasPendingDepts = tx.pendingDepartmentNames.length > 0;
  const canRegisterResponse = canClose && needsResponse && !tx.responseCompleted && !isTerminal;
  const canShowClose = canClose && !isTerminal && (!needsResponse || tx.responseCompleted);

  return (
    <div>
      <div className="page-header">
        <h2 className="page-title">تفاصيل المعاملة: {tx.incomingNumber}</h2>
        <div className="btn-group">
          {canEdit && !isDepartmentUser && <Link to={`/transactions/${id}/edit`} className="btn btn-primary">تعديل</Link>}
          {canRegisterResponse && (
            <button
              type="button"
              className="btn btn-primary"
              disabled={hasPendingDepts}
              title={hasPendingDepts ? 'لا يمكن تسجيل الإفادة قبل اكتمال رد جميع الإدارات.' : undefined}
              onClick={() => setShowCompleteResponse(true)}
            >
              تسجيل الإفادة
            </button>
          )}
          {canShowClose && <button type="button" onClick={handleClose} className="btn btn-danger">إغلاق المعاملة</button>}
        </div>
      </div>

      {message && <div className="alert alert-success">{message}</div>}
      {error && <div className="alert alert-error">{error}</div>}

      <div className={`status-bar badge ${statusBadgeClass(tx.status, tx.isOverdue)}`}>
        <strong>الحالة:</strong> {statusLabels[tx.status] || tx.status}
        {tx.isOverdue && <span className="status-extra"> — متأخرة</span>}
        {tx.hasPendingAssignments && <span className="status-extra"> — باقي إجراء</span>}
        {tx.daysRemainingForResponse != null && !tx.responseCompleted && (
          <span className="status-extra"> — متبقي {tx.daysRemainingForResponse} يوم للإفادة</span>
        )}
      </div>

      <div className="card mt-4">
        <div className="detail-grid">
          <div><strong>رقم التتبع:</strong> {tx.internalTrackingNumber}</div>
          <div><strong>رقم الوارد:</strong> {tx.incomingNumber}</div>
          <div><strong>تاريخ الوارد:</strong> <DateDisplay date={tx.incomingDate} /></div>
          <div><strong>الأولوية:</strong> {priorityLabels[tx.priority]}</div>
          <div><strong>التصنيف:</strong> {tx.categoryName || '-'}</div>
          <div><strong>نوع الجهة الوارد منها:</strong> {tx.incomingSourceType === 'Internal' ? 'داخلية' : 'خارجية'}</div>
          <div><strong>الجهة الوارد منها:</strong> {tx.incomingFrom || '-'}</div>
          <div className="full-width"><strong>الموضوع:</strong> {tx.subject}</div>
          {tx.outgoingNumber && <div><strong>رقم الصادر:</strong> {tx.outgoingNumber}</div>}
          {tx.outgoingDate && <div><strong>تاريخ الصادر:</strong> <DateDisplay date={tx.outgoingDate} /></div>}
          <div className="full-width">
            <strong>الإدارات الصادر لها:</strong>{' '}
            {tx.outgoingDepartments.length > 0 ? tx.outgoingDepartments.map((o) => o.departmentName).join('، ') : '-'}
          </div>
          {needsResponse && (
            <>
              <div><strong>مطلوب إفادة:</strong> نعم ({responseTypeLabels[tx.responseType] || tx.responseType})</div>
              {tx.responseDueDate && <div><strong>تاريخ استحقاق الإفادة:</strong> <DateDisplay date={tx.responseDueDate} /></div>}
              <div>
                <strong>حالة الإفادة:</strong>{' '}
                {tx.responseCompleted
                  ? <>تمت الإفادة{tx.responseCompletedDate && <> بتاريخ <DateDisplay date={tx.responseCompletedDate} /></>}</>
                  : 'لم تتم الإفادة'}
              </div>
              {tx.responseSummary && <div className="full-width"><strong>ملخص الإفادة:</strong> {tx.responseSummary}</div>}
              {hasPendingDepts && !tx.responseCompleted && (
                <div className="full-width overdue-text">لا يمكن تسجيل الإفادة قبل اكتمال رد جميع الإدارات.</div>
              )}
            </>
          )}
          {tx.repliedDepartmentNames.length > 0 && (
            <div className="full-width"><strong>الإدارات التي ردت:</strong> {tx.repliedDepartmentNames.join('، ')}</div>
          )}
          {tx.pendingDepartmentNames.length > 0 && (
            <div className="full-width overdue-text"><strong>الإدارات التي لم ترد:</strong> {tx.pendingDepartmentNames.join('، ')}</div>
          )}
          {tx.notes && <div className="full-width"><strong>ملاحظات:</strong> {tx.notes}</div>}
        </div>
      </div>

      <div className="card mt-4">
        <div className="card-header">
          <h3>التحويلات</h3>
          {canEdit && !isDepartmentUser && <button className="btn btn-sm btn-primary" onClick={() => setShowAssignment(true)}>إضافة تحويل</button>}
        </div>
        <table className="data-table">
          <thead><tr><th>الإدارة</th><th>الإجراء</th><th>تاريخ الاستحقاق</th><th>حالة الرد</th><th>إجراء</th></tr></thead>
          <tbody>
            {tx.assignments.map((a) => (
              <tr key={a.id} className={a.isOverdue ? 'row-overdue' : ''}>
                <td>{a.departmentName}</td>
                <td>{a.requiredAction || '-'}</td>
                <td>{a.dueDate ? <DateDisplay date={a.dueDate} /> : '-'}</td>
                <td><span className={`badge ${a.replyStatus === 'Replied' ? 'badge-green' : a.isOverdue ? 'badge-red' : 'badge-orange'}`}>{a.replyStatus}</span></td>
                <td>
                  {a.requiresReply && a.replyStatus !== 'Replied' && (isDepartmentUser || canEdit) && (
                    <button className="btn btn-sm" onClick={() => setReplyAssignmentId(a.id)}>تسجيل رد</button>
                  )}
                </td>
              </tr>
            ))}
            {tx.assignments.length === 0 && <tr><td colSpan={5} className="text-center">لا توجد تحويلات</td></tr>}
          </tbody>
        </table>
      </div>

      <div className="card mt-4">
        <div className="card-header">
          <h3>التعقيبات</h3>
          {canEdit && !isDepartmentUser && <button className="btn btn-sm btn-primary" onClick={() => setShowFollowUp(true)}>إضافة تعقيب</button>}
        </div>
        <table className="data-table">
          <thead><tr><th>الرقم</th><th>التاريخ</th><th>مرسل إلى</th><th>ملاحظات</th></tr></thead>
          <tbody>
            {tx.followUps.map((f) => (
              <tr key={f.id}>
                <td>{f.followUpNumber || '-'}</td>
                <td><DateDisplay date={f.followUpDate} /></td>
                <td>{f.departments?.length > 0 ? f.departments.map((d) => d.departmentName).join('، ') : f.sentTo || '-'}</td>
                <td>{f.notes || '-'}</td>
              </tr>
            ))}
            {tx.followUps.length === 0 && <tr><td colSpan={4} className="text-center">لا توجد تعقيبات</td></tr>}
          </tbody>
        </table>
      </div>

      <div className="card mt-4">
        <div className="card-header">
          <h3>المرفقات</h3>
          {canEdit && <label className="btn btn-sm btn-primary">رفع ملف<input type="file" hidden onChange={handleFileUpload} /></label>}
        </div>
        <table className="data-table">
          <thead><tr><th>الملف</th><th>الحجم</th><th>رفع بواسطة</th><th>التاريخ</th><th>تحميل</th></tr></thead>
          <tbody>
            {tx.attachments.map((a) => (
              <tr key={a.id}>
                <td>{a.originalFileName}</td>
                <td>{(a.fileSize / 1024).toFixed(1)} KB</td>
                <td>{a.uploadedByName}</td>
                <td><DateDisplay date={a.uploadedAt} /></td>
                <td><button className="btn btn-sm" onClick={() => downloadAttachment(a.id, a.originalFileName)}>تحميل</button></td>
              </tr>
            ))}
            {tx.attachments.length === 0 && <tr><td colSpan={5} className="text-center">لا توجد مرفقات</td></tr>}
          </tbody>
        </table>
      </div>

      <div className="card mt-4">
        <h3>سجل التدقيق</h3>
        <table className="data-table">
          <thead><tr><th>الإجراء</th><th>المستخدم</th><th>التاريخ</th><th>التفاصيل</th></tr></thead>
          <tbody>
            {tx.auditLogs.map((log) => (
              <tr key={log.id}>
                <td>{auditActionLabels[log.action] || log.action}</td>
                <td>{log.userName}</td>
                <td><DateDisplay date={log.createdAt} /></td>
                <td>{log.newValue || log.oldValue || '-'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {showFollowUp && (
        <FollowUpModal
          transactionId={+id!}
          onClose={() => { setShowFollowUp(false); load(); }}
        />
      )}
      {showAssignment && <AssignmentModal transactionId={+id!} departments={departments} onClose={() => { setShowAssignment(false); load(); }} />}
      {replyAssignmentId && <ReplyModal transactionId={+id!} assignmentId={replyAssignmentId} onClose={() => { setReplyAssignmentId(null); load(); }} />}
      {showCompleteResponse && (
        <CompleteResponseModal
          transactionId={+id!}
          responseType={tx.responseType}
          onClose={() => { setShowCompleteResponse(false); load(); }}
          onSuccess={() => setMessage('تم تسجيل الإفادة بنجاح.')}
        />
      )}
    </div>
  );
}

function CompleteResponseModal({
  transactionId,
  responseType,
  onClose,
  onSuccess,
}: {
  transactionId: number;
  responseType: string;
  onClose: () => void;
  onSuccess: () => void;
}) {
  const requiresOutgoing = responseType === 'External' || responseType === 'Both';
  const [form, setForm] = useState({
    responseDate: new Date().toISOString().split('T')[0],
    responseSummary: '',
    outgoingNumber: '',
    outgoingDate: new Date().toISOString().split('T')[0],
  });
  const [attachment, setAttachment] = useState<File | null>(null);
  const [error, setError] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (isSubmitting) return;
    if (!form.responseSummary.trim()) {
      setError('ملخص الإفادة مطلوب');
      return;
    }
    if (requiresOutgoing && (!form.outgoingNumber.trim() || !form.outgoingDate)) {
      setError('رقم الصادر وتاريخ الصادر مطلوبان لنوع الإفادة المحدد');
      return;
    }
    setError('');
    setIsSubmitting(true);
    try {
      await transactionsApi.completeResponse(
        transactionId,
        buildCompleteResponsePayload({ ...form, requiresOutgoing }),
      );
      if (attachment) {
        await transactionsApi.uploadAttachment(transactionId, attachment, 'Response');
      }
      onSuccess();
      onClose();
    } catch (err: unknown) {
      setError(getApiErrorMessage(err));
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="modal-overlay"><div className="modal">
      <h3>تسجيل الإفادة</h3>
      {error && <div className="alert alert-error">{error}</div>}
      <form onSubmit={submit}>
        <div className="form-group">
          <label>تاريخ الإفادة *</label>
          <input type="date" required value={form.responseDate}
            onChange={(e) => setForm({ ...form, responseDate: e.target.value })} />
        </div>
        <div className="form-group">
          <label>ملخص الإفادة *</label>
          <textarea required rows={4} value={form.responseSummary}
            onChange={(e) => setForm({ ...form, responseSummary: e.target.value })} />
        </div>
        {requiresOutgoing && (
          <>
            <div className="form-group">
              <label>رقم الصادر *</label>
              <input required value={form.outgoingNumber}
                onChange={(e) => setForm({ ...form, outgoingNumber: e.target.value })} />
            </div>
            <div className="form-group">
              <label>تاريخ الصادر *</label>
              <input type="date" required value={form.outgoingDate}
                onChange={(e) => setForm({ ...form, outgoingDate: e.target.value })} />
            </div>
          </>
        )}
        <div className="form-group">
          <label>مرفق (اختياري)</label>
          <input type="file" onChange={(e) => setAttachment(e.target.files?.[0] ?? null)} />
        </div>
        <div className="form-actions">
          <button type="submit" className="btn btn-primary" disabled={isSubmitting}>
            {isSubmitting ? 'جاري الحفظ...' : 'تسجيل الإفادة'}
          </button>
          <button type="button" className="btn btn-outline" onClick={onClose}>إلغاء</button>
        </div>
      </form>
    </div></div>
  );
}

function FollowUpModal({ transactionId, onClose }: { transactionId: number; onClose: () => void }) {
  const [options, setOptions] = useState<FollowUpDepartmentOption[]>([]);
  const [loading, setLoading] = useState(true);
  const [form, setForm] = useState({
    followUpDate: new Date().toISOString().split('T')[0],
    notes: '', followUpNumber: '', departmentIds: [] as number[],
  });
  const [error, setError] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);

  useEffect(() => {
    transactionsApi.getFollowUpDepartments(transactionId)
      .then((r) => {
        setOptions(r.data);
        setForm((f) => ({
          ...f,
          departmentIds: r.data.filter((d) => d.isDefaultSelected).map((d) => d.departmentId),
        }));
      })
      .catch(() => setError('تعذر تحميل الإدارات المتاحة'))
      .finally(() => setLoading(false));
  }, [transactionId]);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (isSubmitting) return;
    if (form.departmentIds.length === 0) {
      setError('يجب اختيار إدارة واحدة على الأقل لإرسال التعقيب.');
      return;
    }
    setError('');
    setIsSubmitting(true);
    try {
      await transactionsApi.addFollowUp(transactionId, buildCreateFollowUpPayload(form));
      onClose();
    } catch (err: unknown) {
      setError(getApiErrorMessage(err));
    } finally {
      setIsSubmitting(false);
    }
  };

  if (loading) return <div className="modal-overlay"><div className="modal"><div className="loading">جاري التحميل...</div></div></div>;

  return (
    <div className="modal-overlay"><div className="modal">
      <h3>إضافة تعقيب</h3>
      {error && <div className="alert alert-error">{error}</div>}
      {options.length === 0 ? (
        <p className="alert alert-error">لا توجد إدارات مرتبطة بهذه المعاملة. أضف جهة صادر لها أو تحويلًا قبل إضافة التعقيب.</p>
      ) : (
        <form onSubmit={submit}>
          <div className="form-group"><label>رقم التعقيب</label><input value={form.followUpNumber} onChange={(e) => setForm({ ...form, followUpNumber: e.target.value })} /></div>
          <div className="form-group"><label>التاريخ (ميلادي)</label><input type="date" required value={form.followUpDate} onChange={(e) => setForm({ ...form, followUpDate: e.target.value })} /></div>
          <MultiSelect
            label="مرسل إلى (إدارات)"
            options={options.map((d) => ({ id: d.departmentId, name: d.departmentName }))}
            selected={form.departmentIds}
            onChange={(ids) => setForm({ ...form, departmentIds: ids })}
          />
          <div className="form-group"><label>ملاحظات</label><textarea value={form.notes} onChange={(e) => setForm({ ...form, notes: e.target.value })} /></div>
          <div className="form-actions">
            <button type="submit" className="btn btn-primary" disabled={isSubmitting}>{isSubmitting ? 'جاري الحفظ...' : 'حفظ'}</button>
            <button type="button" className="btn btn-outline" onClick={onClose}>إلغاء</button>
          </div>
        </form>
      )}
      {options.length === 0 && (
        <div className="form-actions"><button type="button" className="btn btn-outline" onClick={onClose}>إغلاق</button></div>
      )}
    </div></div>
  );
}

function AssignmentModal({ transactionId, departments, onClose }: { transactionId: number; departments: Department[]; onClose: () => void }) {
  const [form, setForm] = useState({
    departmentId: '', assignedDate: new Date().toISOString().split('T')[0],
    requiredAction: '', replyDueDays: '' as string | number, dueDate: '',
  });
  const [error, setError] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (isSubmitting) return;
    setError('');
    setIsSubmitting(true);
    try {
      await transactionsApi.addAssignment(transactionId, buildCreateAssignmentPayload(form));
      onClose();
    } catch (err: unknown) {
      setError(getApiErrorMessage(err));
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="modal-overlay"><div className="modal">
      <h3>إضافة تحويل</h3>
      {error && <div className="alert alert-error">{error}</div>}
      <form onSubmit={submit}>
        <div className="form-group"><label>الإدارة *</label>
          <select required value={form.departmentId} onChange={(e) => setForm({ ...form, departmentId: e.target.value })}>
            <option value="">اختر الإدارة</option>
            {departments.map((d) => <option key={d.id} value={d.id}>{d.name}</option>)}
          </select>
        </div>
        <div className="form-group"><label>تاريخ التحويل</label><input type="date" required value={form.assignedDate} onChange={(e) => setForm({ ...form, assignedDate: e.target.value })} /></div>
        <div className="form-group"><label>الإجراء المطلوب</label><input value={form.requiredAction} onChange={(e) => setForm({ ...form, requiredAction: e.target.value })} /></div>
        <div className="form-group"><label>عدد أيام الرد</label><input type="number" min="1" value={form.replyDueDays} onChange={(e) => setForm({ ...form, replyDueDays: e.target.value })} /></div>
        <div className="form-group"><label>أو تاريخ استحقاق محدد</label><input type="date" value={form.dueDate} onChange={(e) => setForm({ ...form, dueDate: e.target.value })} /></div>
        <div className="form-actions">
          <button type="submit" className="btn btn-primary" disabled={isSubmitting}>{isSubmitting ? 'جاري الحفظ...' : 'حفظ'}</button>
          <button type="button" className="btn btn-outline" onClick={onClose}>إلغاء</button>
        </div>
      </form>
    </div></div>
  );
}

function ReplyModal({ transactionId, assignmentId, onClose }: { transactionId: number; assignmentId: number; onClose: () => void }) {
  const [form, setForm] = useState({ replyDate: new Date().toISOString().split('T')[0], replySummary: '' });
  const [error, setError] = useState('');
  const [isSubmitting, setIsSubmitting] = useState(false);

  const submit = async (e: FormEvent) => {
    e.preventDefault();
    if (isSubmitting) return;
    setError('');
    setIsSubmitting(true);
    try {
      await transactionsApi.replyAssignment(transactionId, assignmentId, buildReplyPayload(form));
      onClose();
    } catch (err: unknown) {
      setError(getApiErrorMessage(err));
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <div className="modal-overlay"><div className="modal">
      <h3>تسجيل رد على التحويل</h3>
      {error && <div className="alert alert-error">{error}</div>}
      <form onSubmit={submit}>
        <div className="form-group"><label>تاريخ الرد</label><input type="date" required value={form.replyDate} onChange={(e) => setForm({ ...form, replyDate: e.target.value })} /></div>
        <div className="form-group"><label>ملخص الرد *</label><textarea required rows={4} value={form.replySummary} onChange={(e) => setForm({ ...form, replySummary: e.target.value })} /></div>
        <div className="form-actions">
          <button type="submit" className="btn btn-primary" disabled={isSubmitting}>{isSubmitting ? 'جاري الحفظ...' : 'حفظ'}</button>
          <button type="button" className="btn btn-outline" onClick={onClose}>إلغاء</button>
        </div>
      </form>
    </div></div>
  );
}
