import { useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import { useNavigate, useParams } from 'react-router-dom';
import { transactionsApi, externalPartiesApi, categoriesApi, departmentsApi } from '../api/services';
import type { ExternalParty, Category, TransactionDetail, Department } from '../api/types';
import {
  buildCreateTransactionPayload,
  buildUpdateTransactionPayload,
  getApiErrorMessage,
  getFieldErrors,
} from '../utils/apiHelpers';
import { formatHijri } from '../utils/dateUtils';
import MultiSelect from '../components/MultiSelect';

interface Props { mode: 'create' | 'edit' }

const OUTGOING_HINT = 'بيانات الصادر غير إلزامية، ولكن يجب إكمالها عند البدء بتعبئتها.';
const OUTGOING_PARTIAL_ERROR = 'عند إدخال أي بيان من بيانات الصادر يجب إكمال رقم الصادر وتاريخ الصادر والإدارة الصادر لها.';

function hasOutgoingData(form: { outgoingNumber: string; outgoingDate: string; outgoingDepartmentIds: number[] }) {
  return Boolean(form.outgoingNumber.trim() || form.outgoingDate || form.outgoingDepartmentIds.length > 0);
}

export default function TransactionForm({ mode }: Props) {
  const { id } = useParams();
  const navigate = useNavigate();
  const [parties, setParties] = useState<ExternalParty[]>([]);
  const [departments, setDepartments] = useState<Department[]>([]);
  const [categories, setCategories] = useState<Category[]>([]);
  const [error, setError] = useState('');
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [loading, setLoading] = useState(mode === 'edit');
  const [form, setForm] = useState({
    incomingNumber: '', incomingDate: new Date().toISOString().split('T')[0],
    subject: '',
    incomingSourceType: 'External' as 'External' | 'Internal',
    incomingFromPartyId: '' as string | number,
    incomingFromDepartmentId: '' as string | number,
    outgoingNumber: '', outgoingDate: '', outgoingDepartmentIds: [] as number[],
    responseType: 'External', responseDueDays: '' as string | number,
    priority: 'Normal', categoryId: '' as string | number, notes: '',
  });

  useEffect(() => {
    externalPartiesApi.getAll().then((r) => setParties(r.data));
    departmentsApi.getAll().then((r) => setDepartments(r.data));
    categoriesApi.getAll().then((r) => setCategories(r.data));
  }, []);

  useEffect(() => {
    if (mode === 'edit' && id) {
      transactionsApi.getById(+id).then((res) => {
        const t: TransactionDetail = res.data;
        const isInternal = t.incomingSourceType === 'Internal';
        setForm({
          incomingNumber: t.incomingNumber,
          incomingDate: t.incomingDate.split('T')[0],
          subject: t.subject,
          incomingSourceType: isInternal ? 'Internal' : 'External',
          incomingFromPartyId: isInternal ? '' : (t.incomingFromPartyId ?? ''),
          incomingFromDepartmentId: isInternal ? (t.incomingFromDepartmentId ?? '') : '',
          outgoingNumber: t.outgoingNumber || '',
          outgoingDate: t.outgoingDate?.split('T')[0] || '',
          outgoingDepartmentIds: t.outgoingDepartments.map((o) => o.departmentId),
          responseType: t.responseType === 'None' ? 'External' : t.responseType,
          responseDueDays: t.responseDueDays ?? '',
          priority: t.priority,
          categoryId: t.categoryId ?? '',
          notes: t.notes || '',
        });
      }).finally(() => setLoading(false));
    }
  }, [mode, id]);

  const handleSourceTypeChange = (incomingSourceType: 'External' | 'Internal') => {
    setForm({
      ...form,
      incomingSourceType,
      incomingFromPartyId: '',
      incomingFromDepartmentId: '',
    });
  };

  const validateClient = (): Record<string, string> => {
    const errs: Record<string, string> = {};
    if (!form.incomingNumber.trim()) errs.incomingNumber = 'رقم الوارد مطلوب';
    if (!form.incomingDate) errs.incomingDate = 'تاريخ الوارد مطلوب';
    if (!form.subject.trim()) errs.subject = 'الموضوع مطلوب';
    if (!form.incomingSourceType) errs.incomingSourceType = 'يجب اختيار نوع الجهة الوارد منها.';
    if (form.incomingSourceType === 'External' && !form.incomingFromPartyId)
      errs.incomingFromPartyId = 'يجب اختيار جهة خارجية عند اختيار النوع خارجي.';
    if (form.incomingSourceType === 'Internal' && !form.incomingFromDepartmentId)
      errs.incomingFromDepartmentId = 'يجب اختيار إدارة عند اختيار النوع داخلي.';
    if (form.incomingFromPartyId && form.incomingFromDepartmentId)
      errs.incomingSourceType = 'لا يمكن اختيار جهة خارجية وإدارة داخلية في نفس الوقت.';
    if (!form.categoryId) errs.categoryId = 'التصنيف مطلوب';
    if (!form.priority) errs.priority = 'الأولوية مطلوبة';

    if (hasOutgoingData(form)) {
      if (!form.outgoingNumber.trim()) errs.outgoingNumber = OUTGOING_PARTIAL_ERROR;
      if (!form.outgoingDate) errs.outgoingDate = OUTGOING_PARTIAL_ERROR;
      if (form.outgoingDepartmentIds.length === 0) errs.outgoingDepartmentIds = OUTGOING_PARTIAL_ERROR;
    }

    if (!form.responseType || form.responseType === 'None') errs.responseType = 'نوع الإفادة مطلوب';
    if (!form.responseDueDays) errs.responseDueDays = 'عدد أيام الرد مطلوب';

    return errs;
  };

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    if (isSubmitting) return;
    setError('');
    const clientErrs = validateClient();
    if (Object.keys(clientErrs).length > 0) {
      setFieldErrors(clientErrs);
      return;
    }
    setFieldErrors({});
    setIsSubmitting(true);
    try {
      if (mode === 'create') {
        const res = await transactionsApi.create(buildCreateTransactionPayload(form));
        navigate(`/transactions/${res.data.id}`);
      } else {
        await transactionsApi.update(+id!, buildUpdateTransactionPayload(form));
        navigate(`/transactions/${id}`);
      }
    } catch (err: unknown) {
      setFieldErrors(getFieldErrors(err));
      setError(getApiErrorMessage(err));
    } finally {
      setIsSubmitting(false);
    }
  };

  const fieldError = (name: string) => fieldErrors[name];

  if (loading) return <div className="loading">جاري التحميل...</div>;

  return (
    <div>
      <h2 className="page-title">{mode === 'create' ? 'إضافة معاملة' : 'تعديل معاملة'}</h2>
      <form onSubmit={handleSubmit} noValidate>
        {error && <div className="alert alert-error">{error}</div>}

        <div className="card section-card">
          <h3 className="section-title">أ. بيانات الوارد</h3>
          <div className="form-grid">
            <div className="form-group">
              <label>رقم الوارد *</label>
              <input value={form.incomingNumber} onChange={(e) => setForm({ ...form, incomingNumber: e.target.value })} />
              {fieldError('incomingNumber') && <span className="field-error">{fieldError('incomingNumber')}</span>}
            </div>
            <div className="form-group">
              <label>تاريخ الوارد * (ميلادي)</label>
              <input type="date" value={form.incomingDate} onChange={(e) => setForm({ ...form, incomingDate: e.target.value })} />
              {form.incomingDate && (
                <small className="text-muted">التاريخ الهجري: {formatHijri(form.incomingDate)}</small>
              )}
              {fieldError('incomingDate') && <span className="field-error">{fieldError('incomingDate')}</span>}
            </div>
            <div className="form-group full-width">
              <label>الموضوع *</label>
              <input value={form.subject} onChange={(e) => setForm({ ...form, subject: e.target.value })} />
              {fieldError('subject') && <span className="field-error">{fieldError('subject')}</span>}
            </div>
            <div className="form-group full-width">
              <label>نوع الجهة الوارد منها *</label>
              <div className="radio-group">
                <label className="radio-label">
                  <input type="radio" name="incomingSourceType" value="External"
                    checked={form.incomingSourceType === 'External'}
                    onChange={() => handleSourceTypeChange('External')} />
                  خارجية
                </label>
                <label className="radio-label">
                  <input type="radio" name="incomingSourceType" value="Internal"
                    checked={form.incomingSourceType === 'Internal'}
                    onChange={() => handleSourceTypeChange('Internal')} />
                  داخلية
                </label>
              </div>
              {fieldError('incomingSourceType') && <span className="field-error">{fieldError('incomingSourceType')}</span>}
            </div>
            <div className="form-group full-width">
              <label>الجهة الوارد منها *</label>
              {form.incomingSourceType === 'External' ? (
                <select value={form.incomingFromPartyId}
                  onChange={(e) => setForm({ ...form, incomingFromPartyId: e.target.value, incomingFromDepartmentId: '' })}>
                  <option value="">-- اختر الجهة الخارجية --</option>
                  {parties.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
                </select>
              ) : (
                <select value={form.incomingFromDepartmentId}
                  onChange={(e) => setForm({ ...form, incomingFromDepartmentId: e.target.value, incomingFromPartyId: '' })}>
                  <option value="">-- اختر الإدارة --</option>
                  {departments.map((d) => <option key={d.id} value={d.id}>{d.name}</option>)}
                </select>
              )}
              {fieldError('incomingFromPartyId') && <span className="field-error">{fieldError('incomingFromPartyId')}</span>}
              {fieldError('incomingFromDepartmentId') && <span className="field-error">{fieldError('incomingFromDepartmentId')}</span>}
            </div>
          </div>
        </div>

        <div className="card section-card mt-4">
          <h3 className="section-title">ب. بيانات الصادر</h3>
          <p className="text-muted mb-2">{OUTGOING_HINT}</p>
          <div className="form-grid">
            <div className="form-group">
              <label>رقم الصادر</label>
              <input value={form.outgoingNumber} onChange={(e) => setForm({ ...form, outgoingNumber: e.target.value })} />
              {fieldError('outgoingNumber') && <span className="field-error">{fieldError('outgoingNumber')}</span>}
            </div>
            <div className="form-group">
              <label>تاريخ الصادر (ميلادي)</label>
              <input type="date" value={form.outgoingDate} onChange={(e) => setForm({ ...form, outgoingDate: e.target.value })} />
              {fieldError('outgoingDate') && <span className="field-error">{fieldError('outgoingDate')}</span>}
            </div>
            <div className="form-group">
              <label>التصنيف *</label>
              <select value={form.categoryId} onChange={(e) => setForm({ ...form, categoryId: e.target.value })}>
                <option value="">-- اختر التصنيف --</option>
                {categories.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
              </select>
              {fieldError('categoryId') && <span className="field-error">{fieldError('categoryId')}</span>}
            </div>
            <div className="form-group">
              <label>الأولوية *</label>
              <select value={form.priority} onChange={(e) => setForm({ ...form, priority: e.target.value })}>
                <option value="Normal">عادي</option>
                <option value="Urgent">عاجل</option>
                <option value="VeryUrgent">عاجل جداً</option>
              </select>
              {fieldError('priority') && <span className="field-error">{fieldError('priority')}</span>}
            </div>
            <div className="form-group full-width">
              <MultiSelect
                label="الجهة الصادر لها (إدارات)"
                options={departments.map((d) => ({ id: d.id, name: d.name }))}
                selected={form.outgoingDepartmentIds}
                onChange={(ids) => setForm({ ...form, outgoingDepartmentIds: ids })}
              />
              {fieldError('outgoingDepartmentIds') && <span className="field-error">{fieldError('outgoingDepartmentIds')}</span>}
            </div>
          </div>
        </div>

        <div className="card section-card mt-4">
          <h3 className="section-title">ج. الإفادة والمهلة</h3>
          <div className="form-grid">
            <div className="form-group">
              <label>نوع الإفادة *</label>
              <select value={form.responseType} onChange={(e) => setForm({ ...form, responseType: e.target.value })}>
                <option value="External">إفادة للجهة</option>
                <option value="Internal">إفادة داخلية</option>
                <option value="Both">إفادة للجهة وداخلية</option>
              </select>
              {fieldError('responseType') && <span className="field-error">{fieldError('responseType')}</span>}
            </div>
            <div className="form-group">
              <label>عدد أيام الرد المطلوبة *</label>
              <input type="number" min="1" value={form.responseDueDays}
                onChange={(e) => setForm({ ...form, responseDueDays: e.target.value })} />
              {fieldError('responseDueDays') && <span className="field-error">{fieldError('responseDueDays')}</span>}
            </div>
            {mode === 'edit' && (
              <p className="text-muted">لتسجيل الإفادة استخدم إجراء «تسجيل الإفادة» من صفحة تفاصيل المعاملة.</p>
            )}
          </div>
        </div>

        <div className="card section-card mt-4">
          <h3 className="section-title">د. ملاحظات</h3>
          <div className="form-group">
            <textarea rows={3} value={form.notes} onChange={(e) => setForm({ ...form, notes: e.target.value })} />
          </div>
        </div>

        <div className="form-actions mt-4">
          <button type="submit" className="btn btn-primary" disabled={isSubmitting}>
            {isSubmitting ? 'جاري الحفظ...' : 'حفظ'}
          </button>
          <button type="button" className="btn btn-outline" onClick={() => navigate(-1)}>إلغاء</button>
        </div>
      </form>
    </div>
  );
}
