import { useEffect, useMemo, useState } from 'react';
import type { FormEvent } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
import {
  recurringTemplatesApi, departmentsApi, categoriesApi, externalPartiesApi,
} from '../api/services';
import type {
  RecurringTemplateListItem, RecurringTemplateDetail, RecurringTemplateTransactionItem,
  Department, Category, ExternalParty,
} from '../api/types';
import { getApiErrorMessage, getFieldErrors, toNullableNumber, toNullableString, toIsoDate } from '../utils/apiHelpers';
import { formatGregorian } from '../utils/dateUtils';
import { FUTURE_EVENT_DATE_MESSAGE, isFutureLocalDate } from '../utils/localDate';
import { buildMonthlyPeriodKey, buildQuarterlyPeriodKey, getExpectedDueDate, getPeriodLabel } from '../utils/recurringPeriod';
import HijriDateInput from '../components/HijriDateInput';
import MultiSelect from '../components/MultiSelect';
import SearchableSelect, { type SelectOption } from '../components/SearchableSelect';
import { FormModal } from '../components/ReferenceDataFormModal';
import { PageHeader, Alert, LoadingInline } from '../components/ui';

const RECURRENCE_LABELS: Record<string, string> = { Monthly: 'شهري', Quarterly: 'ربع سنوي' };
const STATUS_LABELS: Record<string, string> = { Active: 'نشط', Paused: 'موقوف', Terminated: 'منتهٍ' };
const STATUS_CLASS: Record<string, string> = { Active: 'badge-green', Paused: 'badge-yellow', Terminated: 'badge-gray' };

type TemplateFormState = {
  title: string;
  subjectTemplate: string;
  recurrenceType: 'Monthly' | 'Quarterly';
  startDate: string;
  endDate: string;
  incomingSourceType: 'External' | 'Internal';
  incomingFromPartyId: string | number;
  incomingFromDepartmentId: string | number;
  categoryId: string | number;
  priority: string;
  responseType: string;
  requiresResponse: boolean;
  defaultRequiredAction: string;
  dueDaysAfterPeriodEnd: string | number;
  defaultReplyDueDays: string | number;
  notes: string;
  departmentIds: number[];
};

function createInitialTemplateForm(): TemplateFormState {
  return {
    title: '',
    subjectTemplate: '',
    recurrenceType: 'Monthly',
    startDate: '',
    endDate: '',
    incomingSourceType: 'Internal',
    incomingFromPartyId: '',
    incomingFromDepartmentId: '',
    categoryId: '',
    priority: 'Normal',
    responseType: 'Internal',
    requiresResponse: true,
    defaultRequiredAction: '',
    dueDaysAfterPeriodEnd: '',
    defaultReplyDueDays: '',
    notes: '',
    departmentIds: [],
  };
}

function buildTemplateFormState(t: RecurringTemplateDetail): TemplateFormState {
  const isInternal = t.incomingSourceType === 'Internal';
  return {
    title: t.title,
    subjectTemplate: t.subjectTemplate,
    recurrenceType: t.recurrenceType === 'Quarterly' ? 'Quarterly' : 'Monthly',
    startDate: t.startDate.split('T')[0],
    endDate: t.endDate ? t.endDate.split('T')[0] : '',
    incomingSourceType: isInternal ? 'Internal' : 'External',
    incomingFromPartyId: isInternal ? '' : (t.incomingFromPartyId ?? ''),
    incomingFromDepartmentId: isInternal ? (t.incomingFromDepartmentId ?? '') : '',
    categoryId: t.categoryId,
    priority: t.priority,
    responseType: t.responseType,
    requiresResponse: t.requiresResponse,
    defaultRequiredAction: t.defaultRequiredAction,
    dueDaysAfterPeriodEnd: t.dueDaysAfterPeriodEnd,
    defaultReplyDueDays: t.defaultReplyDueDays ?? '',
    notes: t.notes ?? '',
    departmentIds: t.departments.map((d) => d.departmentId),
  };
}

function buildTemplatePayload(form: TemplateFormState) {
  const isExternal = form.incomingSourceType === 'External';
  return {
    title: form.title.trim(),
    subjectTemplate: form.subjectTemplate.trim(),
    recurrenceType: form.recurrenceType,
    startDate: toIsoDate(form.startDate),
    endDate: form.endDate ? toIsoDate(form.endDate) : null,
    incomingSourceType: form.incomingSourceType,
    incomingFromPartyId: isExternal ? toNullableNumber(form.incomingFromPartyId) : null,
    incomingFromDepartmentId: isExternal ? null : toNullableNumber(form.incomingFromDepartmentId),
    categoryId: toNullableNumber(form.categoryId),
    priority: form.priority,
    responseType: form.responseType,
    requiresResponse: form.requiresResponse,
    defaultRequiredAction: form.defaultRequiredAction.trim(),
    dueDaysAfterPeriodEnd: toNullableNumber(form.dueDaysAfterPeriodEnd),
    defaultReplyDueDays: toNullableNumber(form.defaultReplyDueDays),
    notes: toNullableString(form.notes),
    departmentIds: form.departmentIds,
  };
}

type GenerateFormState = {
  monthValue: string;
  quarterYear: string;
  quarterNumber: '1' | '2' | '3' | '4';
  incomingDate: string;
  referralDate: string;
  referralLetterNumber: string;
};

function createInitialGenerateForm(nextPeriodKey: string, recurrenceType: string): GenerateFormState {
  if (recurrenceType === 'Quarterly') {
    const match = /^(\d{4})-Q([1-4])$/.exec(nextPeriodKey);
    return {
      monthValue: '',
      quarterYear: match ? match[1] : String(new Date().getFullYear()),
      quarterNumber: (match ? match[2] : '1') as GenerateFormState['quarterNumber'],
      incomingDate: '',
      referralDate: '',
      referralLetterNumber: '',
    };
  }
  return {
    monthValue: nextPeriodKey || '',
    quarterYear: String(new Date().getFullYear()),
    quarterNumber: '1',
    incomingDate: '',
    referralDate: '',
    referralLetterNumber: '',
  };
}

function getPeriodKeyFromGenerateForm(form: GenerateFormState, recurrenceType: string): string {
  return recurrenceType === 'Quarterly'
    ? buildQuarterlyPeriodKey(Number(form.quarterYear), Number(form.quarterNumber))
    : buildMonthlyPeriodKey(form.monthValue);
}

export default function RecurringTemplatesPage() {
  const [searchParams] = useSearchParams();
  const [templates, setTemplates] = useState<RecurringTemplateListItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  const [departments, setDepartments] = useState<Department[]>([]);
  const [categories, setCategories] = useState<Category[]>([]);
  const [parties, setParties] = useState<ExternalParty[]>([]);

  const [formModalMode, setFormModalMode] = useState<'create' | number | null>(null);
  const [form, setForm] = useState<TemplateFormState>(() => createInitialTemplateForm());
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [formError, setFormError] = useState('');
  const [submitting, setSubmitting] = useState(false);

  const [generateTemplate, setGenerateTemplate] = useState<RecurringTemplateDetail | null>(null);
  const [generateForm, setGenerateForm] = useState<GenerateFormState | null>(null);
  const [generateFieldErrors, setGenerateFieldErrors] = useState<Record<string, string>>({});
  const [generateError, setGenerateError] = useState('');
  const [generateConflictTransactionId, setGenerateConflictTransactionId] = useState<number | null>(null);
  const [generateSubmitting, setGenerateSubmitting] = useState(false);
  const [generateSuccess, setGenerateSuccess] = useState<{ transactionId: number; internalTrackingNumber: string } | null>(null);

  const [terminateTemplateId, setTerminateTemplateId] = useState<number | null>(null);
  const [terminateReason, setTerminateReason] = useState('');
  const [terminateError, setTerminateError] = useState('');
  const [terminateSubmitting, setTerminateSubmitting] = useState(false);

  const [expandedTemplateId, setExpandedTemplateId] = useState<number | null>(null);
  const [expandedTransactions, setExpandedTransactions] = useState<Record<number, RecurringTemplateTransactionItem[]>>({});
  const [expandedLoading, setExpandedLoading] = useState(false);

  const loadTemplates = async () => {
    setLoading(true);
    try {
      const res = await recurringTemplatesApi.getAll();
      setTemplates(res.data);
      setError('');
    } catch (err) {
      setError(getApiErrorMessage(err));
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void loadTemplates();
    void (async () => {
      const results = await Promise.allSettled([
        departmentsApi.getAll(true),
        categoriesApi.getAll(true),
        externalPartiesApi.getAll(true),
      ] as const);
      if (results[0].status === 'fulfilled') setDepartments(results[0].value.data);
      if (results[1].status === 'fulfilled') setCategories(results[1].value.data);
      if (results[2].status === 'fulfilled') setParties(results[2].value.data);
    })();
  }, []);

  useEffect(() => {
    const viewTransactions = searchParams.get('viewTransactions');
    if (viewTransactions) {
      void openTransactionsPanel(Number(viewTransactions));
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [searchParams]);

  const departmentOptions = useMemo(
    () => departments.map((d) => ({ id: d.id, name: d.name, isActive: d.isActive })),
    [departments],
  );
  const categoryOptions: SelectOption[] = useMemo(
    () => categories.map((c) => ({ id: c.id, name: c.name, isActive: c.isActive })),
    [categories],
  );
  const partyOptions: SelectOption[] = useMemo(
    () => parties.map((p) => ({ id: p.id, name: p.name, isActive: p.isActive })),
    [parties],
  );

  const openCreateModal = () => {
    setForm(createInitialTemplateForm());
    setFieldErrors({});
    setFormError('');
    setFormModalMode('create');
  };

  const openEditModal = async (id: number) => {
    try {
      const res = await recurringTemplatesApi.getById(id);
      setForm(buildTemplateFormState(res.data));
      setFieldErrors({});
      setFormError('');
      setFormModalMode(id);
    } catch (err) {
      setError(getApiErrorMessage(err));
    }
  };

  const closeFormModal = () => setFormModalMode(null);

  const submitForm = async (e: FormEvent) => {
    e.preventDefault();
    if (submitting) return;
    setSubmitting(true);
    setFormError('');
    setFieldErrors({});
    try {
      const payload = buildTemplatePayload(form);
      if (formModalMode === 'create') {
        await recurringTemplatesApi.create(payload);
      } else if (typeof formModalMode === 'number') {
        await recurringTemplatesApi.update(formModalMode, payload);
      }
      setFormModalMode(null);
      await loadTemplates();
    } catch (err) {
      const errs = getFieldErrors(err);
      if (Object.keys(errs).length > 0) {
        setFieldErrors(errs);
      } else {
        setFormError(getApiErrorMessage(err));
      }
    } finally {
      setSubmitting(false);
    }
  };

  const openGenerateModal = async (id: number) => {
    try {
      const res = await recurringTemplatesApi.getById(id);
      setGenerateTemplate(res.data);
      setGenerateForm(createInitialGenerateForm(res.data.nextPeriodKey, res.data.recurrenceType));
      setGenerateFieldErrors({});
      setGenerateError('');
      setGenerateConflictTransactionId(null);
      setGenerateSuccess(null);
    } catch (err) {
      setError(getApiErrorMessage(err));
    }
  };

  const closeGenerateModal = () => {
    setGenerateTemplate(null);
    setGenerateForm(null);
  };

  const expectedDueDate = useMemo(() => {
    if (!generateTemplate || !generateForm) return null;
    const periodKey = getPeriodKeyFromGenerateForm(generateForm, generateTemplate.recurrenceType);
    return getExpectedDueDate(generateTemplate.recurrenceType, periodKey, generateTemplate.dueDaysAfterPeriodEnd);
  }, [generateTemplate, generateForm]);

  const expectedPeriodLabel = useMemo(() => {
    if (!generateTemplate || !generateForm) return '';
    const periodKey = getPeriodKeyFromGenerateForm(generateForm, generateTemplate.recurrenceType);
    return getPeriodLabel(generateTemplate.recurrenceType, periodKey);
  }, [generateTemplate, generateForm]);

  const submitGenerate = async (e: FormEvent) => {
    e.preventDefault();
    if (!generateTemplate || !generateForm || generateSubmitting) return;

    const errs: Record<string, string> = {};
    if (!generateForm.incomingDate) errs.incomingDate = 'تاريخ المعاملة مطلوب.';
    else if (isFutureLocalDate(generateForm.incomingDate)) errs.incomingDate = FUTURE_EVENT_DATE_MESSAGE;
    if (!generateForm.referralDate) errs.referralDate = 'تاريخ الإحالة مطلوب.';
    else if (isFutureLocalDate(generateForm.referralDate)) errs.referralDate = FUTURE_EVENT_DATE_MESSAGE;

    const periodKey = getPeriodKeyFromGenerateForm(generateForm, generateTemplate.recurrenceType);
    if (!periodKey) errs.periodKey = 'الفترة مطلوبة.';

    if (Object.keys(errs).length > 0) {
      setGenerateFieldErrors(errs);
      return;
    }

    setGenerateSubmitting(true);
    setGenerateError('');
    setGenerateFieldErrors({});
    setGenerateConflictTransactionId(null);
    try {
      const res = await recurringTemplatesApi.generate(generateTemplate.id, {
        periodKey,
        incomingDate: toIsoDate(generateForm.incomingDate),
        referralDate: toIsoDate(generateForm.referralDate),
        referralLetterNumber: toNullableString(generateForm.referralLetterNumber),
      });
      setGenerateSuccess({ transactionId: res.data.transactionId, internalTrackingNumber: res.data.internalTrackingNumber });
      setExpandedTransactions((prev) => {
        const next = { ...prev };
        delete next[generateTemplate.id];
        return next;
      });
      await loadTemplates();
    } catch (err: unknown) {
      const details = err as { response?: { status?: number; data?: { existingTransactionId?: number } } };
      if (details.response?.status === 409) {
        setGenerateConflictTransactionId(details.response.data?.existingTransactionId ?? null);
        setGenerateError(getApiErrorMessage(err));
      } else {
        const errs2 = getFieldErrors(err);
        if (Object.keys(errs2).length > 0) setGenerateFieldErrors(errs2);
        else setGenerateError(getApiErrorMessage(err));
      }
    } finally {
      setGenerateSubmitting(false);
    }
  };

  const handlePause = async (id: number) => {
    try {
      await recurringTemplatesApi.pause(id);
      await loadTemplates();
    } catch (err) {
      setError(getApiErrorMessage(err));
    }
  };

  const handleResume = async (id: number) => {
    try {
      await recurringTemplatesApi.resume(id);
      await loadTemplates();
    } catch (err) {
      setError(getApiErrorMessage(err));
    }
  };

  const openTerminateModal = (id: number) => {
    setTerminateTemplateId(id);
    setTerminateReason('');
    setTerminateError('');
  };

  const submitTerminate = async (e: FormEvent) => {
    e.preventDefault();
    if (terminateTemplateId === null || terminateSubmitting) return;
    if (!terminateReason.trim()) {
      setTerminateError('سبب الإنهاء مطلوب.');
      return;
    }
    setTerminateSubmitting(true);
    setTerminateError('');
    try {
      await recurringTemplatesApi.terminate(terminateTemplateId, terminateReason.trim());
      setTerminateTemplateId(null);
      await loadTemplates();
    } catch (err) {
      setTerminateError(getApiErrorMessage(err));
    } finally {
      setTerminateSubmitting(false);
    }
  };

  async function openTransactionsPanel(id: number) {
    if (expandedTemplateId === id) {
      setExpandedTemplateId(null);
      return;
    }
    setExpandedTemplateId(id);
    if (expandedTransactions[id]) {
      return;
    }
    setExpandedLoading(true);
    try {
      const res = await recurringTemplatesApi.getTransactions(id);
      setExpandedTransactions((prev) => ({ ...prev, [id]: res.data }));
    } catch (err) {
      setError(getApiErrorMessage(err));
    } finally {
      setExpandedLoading(false);
    }
  }

  if (loading) {
    return <div className="loading"><LoadingInline label="جاري التحميل..." /></div>;
  }

  return (
    <div>
      <PageHeader
        title="الالتزامات الدورية"
        actions={<button type="button" className="btn btn-primary" onClick={openCreateModal}>إنشاء قالب دوري جديد</button>}
      />
      {error && <Alert variant="error">{error}</Alert>}

      <div className="table-wrapper">
        <table className="data-table">
          <thead>
            <tr>
              <th>اسم القالب</th>
              <th>نوع التكرار</th>
              <th>الحالة</th>
              <th>بداية التكرار</th>
              <th>نهاية التكرار</th>
              <th>الفترة القادمة</th>
              <th>آخر فترة منشأة</th>
              <th>عدد المعاملات</th>
              <th>إجراءات</th>
            </tr>
          </thead>
          <tbody>
            {templates.length === 0 && (
              <tr><td colSpan={9} className="text-center text-muted">لا توجد قوالب التزامات دورية</td></tr>
            )}
            {templates.map((t) => (
              <RecurringTemplateRow
                key={t.id}
                template={t}
                highlighted={searchParams.get('highlight') === String(t.id)}
                isExpanded={expandedTemplateId === t.id}
                expandedTransactions={expandedTransactions[t.id] || null}
                expandedLoading={expandedTemplateId === t.id && expandedLoading}
                onEdit={() => openEditModal(t.id)}
                onGenerate={() => openGenerateModal(t.id)}
                onPause={() => handlePause(t.id)}
                onResume={() => handleResume(t.id)}
                onTerminate={() => openTerminateModal(t.id)}
                onToggleTransactions={() => openTransactionsPanel(t.id)}
              />
            ))}
          </tbody>
        </table>
      </div>

      {formModalMode !== null && (
        <TemplateFormModal
          mode={formModalMode}
          form={form}
          setForm={setForm}
          fieldErrors={fieldErrors}
          formError={formError}
          submitting={submitting}
          departmentOptions={departmentOptions}
          categoryOptions={categoryOptions}
          partyOptions={partyOptions}
          onSubmit={submitForm}
          onClose={closeFormModal}
        />
      )}

      {generateTemplate && generateForm && (
        <GeneratePeriodModal
          template={generateTemplate}
          form={generateForm}
          setForm={setGenerateForm}
          fieldErrors={generateFieldErrors}
          error={generateError}
          conflictTransactionId={generateConflictTransactionId}
          success={generateSuccess}
          submitting={generateSubmitting}
          expectedDueDate={expectedDueDate}
          expectedPeriodLabel={expectedPeriodLabel}
          onSubmit={submitGenerate}
          onClose={closeGenerateModal}
        />
      )}

      {terminateTemplateId !== null && (
        <FormModal
          title="إنهاء الالتزام الدوري"
          onClose={() => setTerminateTemplateId(null)}
          onSubmit={submitTerminate}
          submitting={terminateSubmitting}
          submitLabel="تأكيد الإنهاء"
        >
          <Alert variant="warning">
            إنهاء الالتزام الدوري سيمنع إنشاء فترات جديدة فقط، ولن يغلق المعاملات المنشأة سابقًا.
          </Alert>
          {terminateError && <Alert variant="error">{terminateError}</Alert>}
          <div className="form-group">
            <label htmlFor="terminate-reason">سبب الإنهاء *</label>
            <textarea
              id="terminate-reason"
              rows={3}
              value={terminateReason}
              onChange={(e) => setTerminateReason(e.target.value)}
            />
          </div>
        </FormModal>
      )}
    </div>
  );
}

function getGenerateDisabledReason(status: string): string | undefined {
  if (status === 'Paused') return 'القالب الدوري موقوف مؤقتًا.';
  if (status === 'Terminated') return 'تم إنهاء هذا الالتزام الدوري ولا يمكن إنشاء فترات جديدة منه.';
  return undefined;
}

function RecurringTemplateRow({
  template, highlighted, isExpanded, expandedTransactions, expandedLoading,
  onEdit, onGenerate, onPause, onResume, onTerminate, onToggleTransactions,
}: Readonly<{
  template: RecurringTemplateListItem;
  highlighted: boolean;
  isExpanded: boolean;
  expandedTransactions: RecurringTemplateTransactionItem[] | null;
  expandedLoading: boolean;
  onEdit: () => void;
  onGenerate: () => void;
  onPause: () => void;
  onResume: () => void;
  onTerminate: () => void;
  onToggleTransactions: () => void;
}>) {
  const isActive = template.status === 'Active';
  const isPaused = template.status === 'Paused';
  const isTerminated = template.status === 'Terminated';
  const generateDisabledReason = getGenerateDisabledReason(template.status);

  return (
    <>
      <tr className={highlighted ? 'row-highlighted' : undefined}>
        <td>{template.title}</td>
        <td>{RECURRENCE_LABELS[template.recurrenceType] ?? template.recurrenceType}</td>
        <td><span className={`badge ${STATUS_CLASS[template.status] ?? 'badge-gray'}`}>{STATUS_LABELS[template.status] ?? template.status}</span></td>
        <td>{formatGregorian(template.startDate)}</td>
        <td>{template.endDate ? formatGregorian(template.endDate) : '—'}</td>
        <td>{template.nextPeriodLabel}</td>
        <td>{template.lastGeneratedPeriodLabel ?? '—'}</td>
        <td>{template.generatedTransactionsCount}</td>
        <td className="table-actions">
          <button type="button" className="btn btn-sm btn-outline" onClick={onEdit}>تعديل</button>
          <button
            type="button"
            className="btn btn-sm btn-primary"
            onClick={onGenerate}
            disabled={!isActive}
            title={generateDisabledReason}
          >
            إنشاء معاملة للفترة
          </button>
          {isActive && <button type="button" className="btn btn-sm btn-outline" onClick={onPause}>إيقاف مؤقت</button>}
          {isPaused && <button type="button" className="btn btn-sm btn-outline" onClick={onResume}>إعادة تفعيل</button>}
          {!isTerminated && <button type="button" className="btn btn-sm btn-outline btn-danger" onClick={onTerminate}>إنهاء الاستمرار</button>}
          <button type="button" className="btn btn-sm btn-ghost" onClick={onToggleTransactions}>
            {isExpanded ? 'إخفاء المعاملات' : 'عرض المعاملات'}
          </button>
        </td>
      </tr>
      {isExpanded && (
        <tr>
          <td colSpan={9}>
            {expandedLoading && <LoadingInline label="جاري تحميل المعاملات..." />}
            {!expandedLoading && expandedTransactions?.length === 0 && (
              <p className="text-muted">لا توجد معاملات منشأة من هذا القالب بعد.</p>
            )}
            {!expandedLoading && expandedTransactions && expandedTransactions.length > 0 && (
              <table className="data-table data-table--nested">
                <thead>
                  <tr>
                    <th>رقم المعاملة</th>
                    <th>الفترة</th>
                    <th>الموضوع</th>
                    <th>الحالة</th>
                    <th></th>
                  </tr>
                </thead>
                <tbody>
                  {expandedTransactions.map((tx) => (
                    <tr key={tx.transactionId}>
                      <td>{tx.internalTrackingNumber}</td>
                      <td>{tx.periodLabel}</td>
                      <td>{tx.subject}</td>
                      <td>{tx.status}</td>
                      <td><Link to={`/transactions/${tx.transactionId}`} className="btn btn-sm btn-outline">عرض</Link></td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </td>
        </tr>
      )}
    </>
  );
}

function TemplateFormModal({
  mode, form, setForm, fieldErrors, formError, submitting,
  departmentOptions, categoryOptions, partyOptions, onSubmit, onClose,
}: Readonly<{
  mode: 'create' | number;
  form: TemplateFormState;
  setForm: (form: TemplateFormState) => void;
  fieldErrors: Record<string, string>;
  formError: string;
  submitting: boolean;
  departmentOptions: { id: number; name: string; isActive?: boolean }[];
  categoryOptions: SelectOption[];
  partyOptions: SelectOption[];
  onSubmit: (e: FormEvent) => void;
  onClose: () => void;
}>) {
  const fieldError = (name: string) => fieldErrors[name];
  const isExternal = form.incomingSourceType === 'External';

  return (
    <FormModal
      title={mode === 'create' ? 'إنشاء قالب التزام دوري' : 'تعديل قالب الالتزام الدوري'}
      onClose={onClose}
      onSubmit={onSubmit}
      submitting={submitting}
      submitLabel="حفظ"
    >
      {formError && <Alert variant="error">{formError}</Alert>}

      <div className="form-group">
        <label htmlFor="template-title">اسم القالب *</label>
        <input id="template-title" value={form.title} onChange={(e) => setForm({ ...form, title: e.target.value })} />
        {fieldError('Title') && <span className="field-error">{fieldError('Title')}</span>}
      </div>

      <div className="form-group">
        <label htmlFor="template-subject">موضوع المعاملة *</label>
        <input id="template-subject" value={form.subjectTemplate} onChange={(e) => setForm({ ...form, subjectTemplate: e.target.value })} />
        {fieldError('SubjectTemplate') && <span className="field-error">{fieldError('SubjectTemplate')}</span>}
      </div>

      <div className="form-group">
        <label htmlFor="template-recurrence">نوع التكرار *</label>
        <select
          id="template-recurrence"
          value={form.recurrenceType}
          onChange={(e) => setForm({ ...form, recurrenceType: e.target.value as TemplateFormState['recurrenceType'] })}
        >
          <option value="Monthly">شهري</option>
          <option value="Quarterly">ربع سنوي</option>
        </select>
        {fieldError('RecurrenceType') && <span className="field-error">{fieldError('RecurrenceType')}</span>}
      </div>

      <div className="form-group">
        <HijriDateInput
          id="template-start-date"
          label="تاريخ بداية التكرار"
          required
          value={form.startDate}
          onChange={(startDate) => setForm({ ...form, startDate })}
          invalid={Boolean(fieldError('StartDate'))}
        />
        {fieldError('StartDate') && <span className="field-error">{fieldError('StartDate')}</span>}
      </div>

      <div className="form-group">
        <HijriDateInput
          id="template-end-date"
          label="تاريخ نهاية التكرار"
          value={form.endDate}
          onChange={(endDate) => setForm({ ...form, endDate })}
          invalid={Boolean(fieldError('EndDate'))}
        />
        {fieldError('EndDate') && <span className="field-error">{fieldError('EndDate')}</span>}
      </div>

      <div className="form-group">
        <span className="form-label">مصدر الوارد *</span>
        <div className="radio-group">
          <label className="radio-label">
            <input
              type="radio"
              checked={form.incomingSourceType === 'External'}
              onChange={() => setForm({ ...form, incomingSourceType: 'External', incomingFromDepartmentId: '' })}
            />
            <span>جهة خارجية</span>
          </label>
          <label className="radio-label">
            <input
              type="radio"
              checked={form.incomingSourceType === 'Internal'}
              onChange={() => setForm({ ...form, incomingSourceType: 'Internal', incomingFromPartyId: '' })}
            />
            <span>إدارة داخلية</span>
          </label>
        </div>
      </div>

      <div className="form-group">
        {isExternal ? (
          <SearchableSelect
            label="الجهة الوارد منها"
            required
            value={form.incomingFromPartyId === '' ? '' : Number(form.incomingFromPartyId)}
            onChange={(id) => setForm({ ...form, incomingFromPartyId: id })}
            options={partyOptions}
            invalid={Boolean(fieldError('IncomingFromPartyId'))}
          />
        ) : (
          <SearchableSelect
            label="الإدارة الوارد منها"
            required
            value={form.incomingFromDepartmentId === '' ? '' : Number(form.incomingFromDepartmentId)}
            onChange={(id) => setForm({ ...form, incomingFromDepartmentId: id })}
            options={departmentOptions}
            invalid={Boolean(fieldError('IncomingFromDepartmentId'))}
          />
        )}
        {(fieldError('IncomingFromPartyId') || fieldError('IncomingFromDepartmentId')) && (
          <span className="field-error">{fieldError('IncomingFromPartyId') || fieldError('IncomingFromDepartmentId')}</span>
        )}
      </div>

      <div className="form-group">
        <SearchableSelect
          label="التصنيف"
          required
          value={form.categoryId === '' ? '' : Number(form.categoryId)}
          onChange={(id) => setForm({ ...form, categoryId: id })}
          options={categoryOptions}
          invalid={Boolean(fieldError('CategoryId'))}
        />
        {fieldError('CategoryId') && <span className="field-error">{fieldError('CategoryId')}</span>}
      </div>

      <div className="form-group">
        <label htmlFor="template-priority">الأولوية *</label>
        <select id="template-priority" value={form.priority} onChange={(e) => setForm({ ...form, priority: e.target.value })}>
          <option value="Normal">عادي</option>
          <option value="Urgent">عاجل</option>
          <option value="VeryUrgent">عاجل جداً</option>
        </select>
        {fieldError('Priority') && <span className="field-error">{fieldError('Priority')}</span>}
      </div>

      <div className="form-group">
        <label className="checkbox-label">
          <input
            type="checkbox"
            checked={form.requiresResponse}
            onChange={(e) => setForm({ ...form, requiresResponse: e.target.checked })}
          />
          <span>يتطلب إفادة من الإدارات</span>
        </label>
      </div>

      <div className="form-group">
        <label htmlFor="template-response-type">نوع الإفادة *</label>
        <select id="template-response-type" value={form.responseType} onChange={(e) => setForm({ ...form, responseType: e.target.value })}>
          <option value="Internal">إفادة داخلية</option>
          <option value="External">إفادة للجهة</option>
          <option value="Both">إفادة للجهة وداخلية</option>
        </select>
        {fieldError('ResponseType') && <span className="field-error">{fieldError('ResponseType')}</span>}
      </div>

      <div className="form-group">
        <MultiSelect
          label="الإدارات المطلوب منها الإفادة"
          options={departmentOptions}
          selected={form.departmentIds}
          onChange={(ids) => setForm({ ...form, departmentIds: ids })}
          invalid={Boolean(fieldError('DepartmentIds'))}
        />
        {fieldError('DepartmentIds') && <span className="field-error">{fieldError('DepartmentIds')}</span>}
      </div>

      <div className="form-group">
        <label htmlFor="template-required-action">الإجراء المطلوب الافتراضي *</label>
        <input
          id="template-required-action"
          value={form.defaultRequiredAction}
          onChange={(e) => setForm({ ...form, defaultRequiredAction: e.target.value })}
        />
        {fieldError('DefaultRequiredAction') && <span className="field-error">{fieldError('DefaultRequiredAction')}</span>}
      </div>

      <div className="form-group">
        <label htmlFor="template-due-days">عدد الأيام بعد نهاية الفترة للاستحقاق *</label>
        <input
          id="template-due-days"
          type="number"
          min="0"
          value={form.dueDaysAfterPeriodEnd}
          onChange={(e) => setForm({ ...form, dueDaysAfterPeriodEnd: e.target.value })}
        />
        {fieldError('DueDaysAfterPeriodEnd') && <span className="field-error">{fieldError('DueDaysAfterPeriodEnd')}</span>}
      </div>

      <div className="form-group">
        <label htmlFor="template-notes">ملاحظات</label>
        <textarea id="template-notes" rows={2} value={form.notes} onChange={(e) => setForm({ ...form, notes: e.target.value })} />
      </div>
    </FormModal>
  );
}

function GeneratePeriodModal({
  template, form, setForm, fieldErrors, error, conflictTransactionId, success, submitting,
  expectedDueDate, expectedPeriodLabel, onSubmit, onClose,
}: Readonly<{
  template: RecurringTemplateDetail;
  form: GenerateFormState;
  setForm: (form: GenerateFormState) => void;
  fieldErrors: Record<string, string>;
  error: string;
  conflictTransactionId: number | null;
  success: { transactionId: number; internalTrackingNumber: string } | null;
  submitting: boolean;
  expectedDueDate: string | null;
  expectedPeriodLabel: string;
  onSubmit: (e: FormEvent) => void;
  onClose: () => void;
}>) {
  const isQuarterly = template.recurrenceType === 'Quarterly';

  if (success) {
    return (
      <FormModal title="تم إنشاء المعاملة" onClose={onClose} onSubmit={(e) => { e.preventDefault(); onClose(); }} submitting={false} submitLabel="إغلاق">
        <Alert variant="success">
          تم إنشاء المعاملة رقم {success.internalTrackingNumber} بنجاح.
        </Alert>
        <Link to={`/transactions/${success.transactionId}`} className="btn btn-primary">عرض المعاملة</Link>
      </FormModal>
    );
  }

  return (
    <FormModal
      title={`إنشاء معاملة للفترة — ${template.title}`}
      onClose={onClose}
      onSubmit={onSubmit}
      submitting={submitting}
      submitLabel="إنشاء المعاملة"
    >
      {error && (
        <Alert variant="error">
          {error}
          {conflictTransactionId !== null && (
            <>
              {' '}
              <Link to={`/transactions/${conflictTransactionId}`}>عرض المعاملة الموجودة</Link>
            </>
          )}
        </Alert>
      )}

      <div className="form-group">
        <span className="form-label">الفترة المراد توليدها *</span>
        {isQuarterly ? (
          <div className="flex-row">
            <select
              aria-label="السنة"
              value={form.quarterYear}
              onChange={(e) => setForm({ ...form, quarterYear: e.target.value })}
            >
              {Array.from({ length: 6 }, (_, i) => new Date().getFullYear() - 2 + i).map((y) => (
                <option key={y} value={y}>{y}</option>
              ))}
            </select>
            <select
              aria-label="الربع"
              value={form.quarterNumber}
              onChange={(e) => setForm({ ...form, quarterNumber: e.target.value as GenerateFormState['quarterNumber'] })}
            >
              <option value="1">الربع الأول</option>
              <option value="2">الربع الثاني</option>
              <option value="3">الربع الثالث</option>
              <option value="4">الربع الرابع</option>
            </select>
          </div>
        ) : (
          <input
            type="month"
            aria-label="الشهر"
            value={form.monthValue}
            onChange={(e) => setForm({ ...form, monthValue: e.target.value })}
          />
        )}
        <small className="text-muted">{expectedPeriodLabel}</small>
        {fieldErrors.periodKey && <span className="field-error">{fieldErrors.periodKey}</span>}
      </div>

      <div className="form-group">
        <HijriDateInput
          id="generate-incoming-date"
          label="تاريخ المعاملة"
          required
          value={form.incomingDate}
          onChange={(incomingDate) => setForm({ ...form, incomingDate })}
          invalid={Boolean(fieldErrors.incomingDate)}
          disallowFutureDate
        />
        {fieldErrors.incomingDate && <span className="field-error">{fieldErrors.incomingDate}</span>}
      </div>

      <div className="form-group">
        <HijriDateInput
          id="generate-referral-date"
          label="تاريخ الإحالة"
          required
          value={form.referralDate}
          onChange={(referralDate) => setForm({ ...form, referralDate })}
          invalid={Boolean(fieldErrors.referralDate)}
          disallowFutureDate
        />
        {fieldErrors.referralDate && <span className="field-error">{fieldErrors.referralDate}</span>}
      </div>

      <div className="form-group">
        <label htmlFor="generate-letter-number">رقم خطاب الإحالة للإدارة</label>
        <input
          id="generate-letter-number"
          value={form.referralLetterNumber}
          onChange={(e) => setForm({ ...form, referralLetterNumber: e.target.value })}
        />
      </div>

      <div className="form-group">
        <span className="form-label">تاريخ الاستحقاق المحسوب</span>
        <p>{expectedDueDate ? formatGregorian(expectedDueDate) : '—'}</p>
      </div>

      <div className="form-group">
        <span className="form-label">الإدارات التي ستُحال لها المعاملة</span>
        <ul>
          {template.departments.map((d) => <li key={d.departmentId}>{d.departmentName}</li>)}
        </ul>
      </div>
    </FormModal>
  );
}
