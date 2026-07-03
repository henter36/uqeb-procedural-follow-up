import { useEffect, useMemo, useState } from 'react';
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
import HijriDateInput from '../components/HijriDateInput';
import SearchableSelect, { type SelectOption } from '../components/SearchableSelect';
import { PageHeader, FormSection, Alert, LoadingInline } from '../components/ui';
import { FUTURE_EVENT_DATE_MESSAGE, isFutureLocalDate } from '../utils/localDate';

interface Props { mode: 'create' | 'edit' }

type TransactionFormState = {
  incomingNumber: string;
  incomingDate: string;
  subject: string;
  incomingSourceType: 'External' | 'Internal';
  incomingFromPartyId: string | number;
  incomingFromDepartmentId: string | number;
  outgoingNumber: string;
  outgoingDate: string;
  outgoingDepartmentIds: number[];
  responseType: string;
  responseDueDays: string | number;
  priority: string;
  categoryId: string | number;
  notes: string;
};
type ReferenceDataResults = readonly [
  PromiseSettledResult<{ data: ExternalParty[] }>,
  PromiseSettledResult<{ data: Department[] }>,
  PromiseSettledResult<{ data: Category[] }>,
];
type TransactionValidationRule = {
  field: string;
  isInvalid: boolean;
  message: string;
};

const OUTGOING_HINT = 'بيانات الإحالة غير إلزامية، ولكن يجب إكمالها عند البدء بتعبئتها.';
const OUTGOING_PARTIAL_ERROR = 'عند إدخال أي بيان من بيانات الإحالة يجب إكمال رقم خطاب الإحالة للإدارة وتاريخ الإحالة والإدارة الموجه لها.';
const OUTGOING_DEPARTMENT_ERROR = 'اختر جهة داخلية واحدة على الأقل.';
const OUTGOING_DATE_REQUIRED_WITH_NUMBER_ERROR = 'تاريخ الإحالة مطلوب عند إدخال رقم خطاب الإحالة للإدارة.';
// These mappings keep backend validation keys aligned with TransactionForm field names and error display order.
// Update them when transaction DTO validation fields or form field names change.
const FIELD_ORDER = [
  'incomingNumber',
  'incomingDate',
  'subject',
  'incomingSourceType',
  'incomingFromPartyId',
  'incomingFromDepartmentId',
  'categoryId',
  'priority',
  'responseType',
  'responseDueDays',
  'outgoingNumber',
  'outgoingDate',
  'outgoingDepartmentIds',
];
const BACKEND_FIELD_MAP: Record<string, string> = {
  IncomingNumber: 'incomingNumber',
  IncomingDate: 'incomingDate',
  Subject: 'subject',
  IncomingSourceType: 'incomingSourceType',
  IncomingFromPartyId: 'incomingFromPartyId',
  IncomingFromDepartmentId: 'incomingFromDepartmentId',
  CategoryId: 'categoryId',
  Priority: 'priority',
  ResponseType: 'responseType',
  ResponseDueDays: 'responseDueDays',
  OutgoingNumber: 'outgoingNumber',
  OutgoingDate: 'outgoingDate',
  OutgoingDepartmentIds: 'outgoingDepartmentIds',
};

function normalizeFieldName(name: string) {
  const trimmed = name.split('.').at(-1) ?? name;
  return BACKEND_FIELD_MAP[trimmed] ?? trimmed.charAt(0).toLowerCase() + trimmed.slice(1);
}

function normalizeFieldErrors(errors: Record<string, string>) {
  return Object.fromEntries(
    Object.entries(errors).map(([key, value]) => [normalizeFieldName(key), value]),
  );
}

function hasOutgoingData(form: { outgoingNumber: string; outgoingDate: string; outgoingDepartmentIds: number[] }) {
  return Boolean(form.outgoingNumber.trim() || form.outgoingDate || form.outgoingDepartmentIds.length > 0);
}

function createInitialTransactionForm(): TransactionFormState {
  return {
    incomingNumber: '',
    incomingDate: '',
    subject: '',
    incomingSourceType: 'External',
    incomingFromPartyId: '',
    incomingFromDepartmentId: '',
    outgoingNumber: '',
    outgoingDate: '',
    outgoingDepartmentIds: [],
    responseType: 'External',
    responseDueDays: '',
    priority: 'Normal',
    categoryId: '',
    notes: '',
  };
}

function buildTransactionFormState(t: TransactionDetail): TransactionFormState {
  const isInternal = t.incomingSourceType === 'Internal';
  return {
    incomingNumber: t.incomingNumber,
    incomingDate: t.incomingDate.split('T')[0],
    subject: t.subject,
    incomingSourceType: isInternal ? 'Internal' : 'External',
    incomingFromPartyId: isInternal ? '' : (t.incomingFromPartyId ?? ''),
    incomingFromDepartmentId: isInternal ? (t.incomingFromDepartmentId ?? '') : '',
    outgoingNumber: t.outgoingNumber || '',
    outgoingDate: t.outgoingDate?.split('T')[0] || '',
    outgoingDepartmentIds: t.outgoingDepartments.map((o) => o.departmentId),
    responseType: t.responseType ?? '',
    responseDueDays: t.responseDueDays ?? '',
    priority: t.priority,
    categoryId: t.categoryId ?? '',
    notes: t.notes || '',
  };
}

function isMissingExternalIncomingParty(form: TransactionFormState): boolean {
  return form.incomingSourceType === 'External' && !form.incomingFromPartyId;
}

function isMissingInternalIncomingDepartment(form: TransactionFormState): boolean {
  return form.incomingSourceType === 'Internal' && !form.incomingFromDepartmentId;
}

function hasConflictingIncomingSources(form: TransactionFormState): boolean {
  return Boolean(form.incomingFromPartyId && form.incomingFromDepartmentId);
}

function isMissingResponseType(form: TransactionFormState, mode: Props['mode']): boolean {
  return !form.responseType || (mode === 'create' && form.responseType === 'None');
}

function isMissingResponseDueDays(form: TransactionFormState): boolean {
  return form.responseType !== 'None' && !form.responseDueDays;
}

function isMissingOutgoingNumber(form: TransactionFormState, hasPartialOutgoingData: boolean): boolean {
  return hasPartialOutgoingData && !form.outgoingNumber.trim();
}

function isMissingOutgoingDate(form: TransactionFormState, hasPartialOutgoingData: boolean): boolean {
  return hasPartialOutgoingData && !form.outgoingDate && !form.outgoingNumber.trim();
}

function isMissingOutgoingDateForNumber(form: TransactionFormState): boolean {
  return Boolean(form.outgoingNumber.trim() && !form.outgoingDate);
}

function isMissingOutgoingDepartments(form: TransactionFormState, hasPartialOutgoingData: boolean): boolean {
  return hasPartialOutgoingData && form.outgoingDepartmentIds.length === 0;
}

function isOutgoingBeforeIncoming(form: TransactionFormState): boolean {
  return Boolean(form.incomingDate && form.outgoingDate && form.outgoingDate < form.incomingDate);
}

function getTransactionValidationRules(form: TransactionFormState, mode: Props['mode']): TransactionValidationRule[] {
  const hasPartialOutgoingData = hasOutgoingData(form);

  return [
    { field: 'incomingNumber', isInvalid: !form.incomingNumber.trim(), message: 'رقم المعاملة مطلوب.' },
    { field: 'incomingDate', isInvalid: !form.incomingDate, message: 'تاريخ المعاملة مطلوب.' },
    { field: 'incomingDate', isInvalid: isFutureLocalDate(form.incomingDate), message: FUTURE_EVENT_DATE_MESSAGE },
    { field: 'subject', isInvalid: !form.subject.trim(), message: 'الموضوع مطلوب.' },
    { field: 'incomingSourceType', isInvalid: !form.incomingSourceType, message: 'يجب اختيار نوع الجهة الوارد منها.' },
    { field: 'incomingFromPartyId', isInvalid: isMissingExternalIncomingParty(form), message: 'الجهة الخارجية مطلوبة.' },
    { field: 'incomingFromDepartmentId', isInvalid: isMissingInternalIncomingDepartment(form), message: 'الجهة الداخلية مطلوبة.' },
    { field: 'incomingSourceType', isInvalid: hasConflictingIncomingSources(form), message: 'لا يمكن اختيار جهة خارجية وإدارة داخلية في نفس الوقت.' },
    { field: 'categoryId', isInvalid: !form.categoryId, message: 'التصنيف مطلوب.' },
    { field: 'priority', isInvalid: !form.priority, message: 'الأولوية مطلوبة.' },
    { field: 'outgoingNumber', isInvalid: isMissingOutgoingNumber(form, hasPartialOutgoingData), message: OUTGOING_PARTIAL_ERROR },
    { field: 'outgoingDate', isInvalid: isMissingOutgoingDate(form, hasPartialOutgoingData), message: OUTGOING_PARTIAL_ERROR },
    { field: 'outgoingDate', isInvalid: isMissingOutgoingDateForNumber(form), message: OUTGOING_DATE_REQUIRED_WITH_NUMBER_ERROR },
    { field: 'outgoingDate', isInvalid: isFutureLocalDate(form.outgoingDate), message: FUTURE_EVENT_DATE_MESSAGE },
    { field: 'outgoingDate', isInvalid: isOutgoingBeforeIncoming(form), message: 'تاريخ الإحالة لا يمكن أن يكون قبل تاريخ المعاملة.' },
    { field: 'outgoingDepartmentIds', isInvalid: isMissingOutgoingDepartments(form, hasPartialOutgoingData), message: OUTGOING_DEPARTMENT_ERROR },
    { field: 'responseType', isInvalid: isMissingResponseType(form, mode), message: 'نوع الإفادة مطلوب.' },
    { field: 'responseDueDays', isInvalid: isMissingResponseDueDays(form), message: 'عدد أيام الرد مطلوب عند طلب إفادة.' },
  ];
}

function validateTransactionForm(form: TransactionFormState, mode: Props['mode']): Record<string, string> {
  return getTransactionValidationRules(form, mode).reduce<Record<string, string>>((errs, rule) => {
    if (rule.isInvalid) {
      errs[rule.field] = rule.message;
    }

    return errs;
  }, {});
}

function getValidationSummary(fieldErrors: Record<string, string>) {
  return [
    ...FIELD_ORDER.filter((field) => fieldErrors[field]).map((field) => [field, fieldErrors[field]] as const),
    ...Object.entries(fieldErrors).filter(([field]) => !FIELD_ORDER.includes(field)),
  ];
}

function getFirstInvalidField(errors: Record<string, string>) {
  return FIELD_ORDER.find((field) => errors[field]) ?? Object.keys(errors)[0];
}

function readReferenceDataResults(results: ReferenceDataResults) {
  const [partiesResult, departmentsResult, categoriesResult] = results;
  return {
    parties: partiesResult.status === 'fulfilled' ? partiesResult.value.data : null,
    departments: departmentsResult.status === 'fulfilled' ? departmentsResult.value.data : null,
    categories: categoriesResult.status === 'fulfilled' ? categoriesResult.value.data : null,
    failed: [
      partiesResult.status === 'rejected' ? 'الجهات الخارجية' : '',
      departmentsResult.status === 'rejected' ? 'الإدارات' : '',
      categoriesResult.status === 'rejected' ? 'التصنيفات' : '',
    ].filter(Boolean),
  };
}

function getTransactionFormHeader(mode: Props['mode']) {
  return mode === 'create' ? 'إضافة معاملة' : 'تعديل معاملة';
}

function toSelectValue(value: string | number) {
  return value === '' ? '' : Number(value);
}

function TransactionValidationSummary({
  hasFieldErrors,
  validationSummary,
}: Readonly<{
  hasFieldErrors: boolean;
  validationSummary: readonly (readonly [string, string])[];
}>) {
  if (!hasFieldErrors) return null;

  return (
    <Alert variant="error">
      <div className="validation-summary">
        <strong>يرجى تصحيح الحقول التالية:</strong>
        <ul>
          {validationSummary.map(([field, message]) => (
            <li key={field}>{message}</li>
          ))}
        </ul>
      </div>
    </Alert>
  );
}

function IncomingSourceTypeField({
  value,
  onChange,
  error,
  errorId,
  formGroupClass,
}: Readonly<{
  value: TransactionFormState['incomingSourceType'];
  onChange: (incomingSourceType: TransactionFormState['incomingSourceType']) => void;
  error?: string;
  errorId: string;
  formGroupClass: string;
}>) {
  return (
    <div className={formGroupClass}>
      <span className="form-label">نوع الجهة الوارد منها *</span>
      <div className="radio-group">
        <label className="radio-label">
          <input
            type="radio"
            name="incomingSourceType"
            value="External"
            checked={value === 'External'}
            onChange={() => onChange('External')}
          />
          <span>خارجية</span>
        </label>
        <label className="radio-label">
          <input
            type="radio"
            name="incomingSourceType"
            value="Internal"
            checked={value === 'Internal'}
            onChange={() => onChange('Internal')}
          />
          <span>داخلية</span>
        </label>
      </div>
      {error && <span id={errorId} className="field-error">{error}</span>}
    </div>
  );
}

function IncomingSourceSelector({
  sourceType,
  incomingFromPartyId,
  incomingFromDepartmentId,
  partyOptions,
  departmentOptions,
  partyError,
  departmentError,
  partyErrorId,
  departmentErrorId,
  formGroupClass,
  onPartyChange,
  onDepartmentChange,
}: Readonly<{
  sourceType: TransactionFormState['incomingSourceType'];
  incomingFromPartyId: string | number;
  incomingFromDepartmentId: string | number;
  partyOptions: SelectOption[];
  departmentOptions: SelectOption[];
  partyError?: string;
  departmentError?: string;
  partyErrorId: string;
  departmentErrorId: string;
  formGroupClass: string;
  onPartyChange: (id: number | '') => void;
  onDepartmentChange: (id: number | '') => void;
}>) {
  const isExternal = sourceType === 'External';
  const error = isExternal ? partyError : departmentError;
  const errorId = isExternal ? partyErrorId : departmentErrorId;

  return (
    <div className={formGroupClass}>
      {isExternal ? (
        <SearchableSelect
          label="الجهة الوارد منها"
          required
          value={toSelectValue(incomingFromPartyId)}
          onChange={onPartyChange}
          options={partyOptions}
          invalid={Boolean(partyError)}
          describedBy={partyError ? partyErrorId : undefined}
          dataFieldName="incomingFromPartyId"
        />
      ) : (
        <SearchableSelect
          label="الجهة الوارد منها"
          required
          value={toSelectValue(incomingFromDepartmentId)}
          onChange={onDepartmentChange}
          options={departmentOptions}
          invalid={Boolean(departmentError)}
          describedBy={departmentError ? departmentErrorId : undefined}
          dataFieldName="incomingFromDepartmentId"
        />
      )}
      {error && <span id={errorId} className="field-error">{error}</span>}
    </div>
  );
}

function TransactionFormActions({
  mode,
  isSubmitting,
  onSaveAndOpenAttachments,
  onCancel,
}: Readonly<{
  mode: Props['mode'];
  isSubmitting: boolean;
  onSaveAndOpenAttachments: () => void;
  onCancel: () => void;
}>) {
  const saveLabel = isSubmitting ? 'جاري الحفظ...' : 'حفظ';
  const saveAttachmentsLabel = isSubmitting ? 'جاري الحفظ...' : 'حفظ وفتح المرفقات';

  return (
    <div className="form-actions mt-4">
      <button type="submit" className="btn btn-primary" disabled={isSubmitting}>
        {saveLabel}
      </button>
      {mode === 'create' && (
        <button
          type="button"
          className="btn btn-secondary"
          disabled={isSubmitting}
          onClick={onSaveAndOpenAttachments}
        >
          {saveAttachmentsLabel}
        </button>
      )}
      <button type="button" className="btn btn-outline" onClick={onCancel}>إلغاء</button>
    </div>
  );
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
  const [referenceLoading, setReferenceLoading] = useState(true);
  const [form, setForm] = useState<TransactionFormState>(() => createInitialTransactionForm());
  const header = getTransactionFormHeader(mode);

  const partyOptions: SelectOption[] = useMemo(
    () => parties.map((p) => ({ id: p.id, name: p.name, isActive: p.isActive, subLabel: p.type })),
    [parties],
  );
  const departmentOptions: SelectOption[] = useMemo(
    () => departments.map((d) => ({ id: d.id, name: d.name, isActive: d.isActive, subLabel: d.code })),
    [departments],
  );
  const categoryOptions: SelectOption[] = useMemo(
    () => categories.map((c) => ({ id: c.id, name: c.name, isActive: c.isActive, subLabel: c.code })),
    [categories],
  );

  const computedResponseDueDate = useMemo(() => {
    if (!form.incomingDate || !form.responseDueDays) return null;
    const days = Number(form.responseDueDays);
    if (!Number.isFinite(days) || days <= 0) return null;
    const d = new Date(`${form.incomingDate}T00:00:00`);
    d.setDate(d.getDate() + days);
    return d;
  }, [form.incomingDate, form.responseDueDays]);

  useEffect(() => {
    let cancelled = false;
    const activeOnly = mode === 'create';
    void (async () => {
      setReferenceLoading(true);
      const results = await Promise.allSettled([
        externalPartiesApi.getAll(activeOnly),
        departmentsApi.getAll(activeOnly),
        categoriesApi.getAll(activeOnly),
      ] as const);
      if (cancelled) return;
      const { parties: loadedParties, departments: loadedDepartments, categories: loadedCategories, failed } =
        readReferenceDataResults(results);
      if (loadedParties) setParties(loadedParties);
      if (loadedDepartments) setDepartments(loadedDepartments);
      if (loadedCategories) setCategories(loadedCategories);
      if (failed.length > 0) setError(`تعذر تحميل: ${failed.join('، ')}`);
      setReferenceLoading(false);
    })();
    return () => { cancelled = true; };
  }, [mode]);

  useEffect(() => {
    if (mode === 'edit' && id) {
      transactionsApi.getById(+id).then((res) => {
        setForm(buildTransactionFormState(res.data));
      }).catch(() => {
        setError('تعذر تحميل المعاملة');
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

  const focusFirstInvalidField = (errors: Record<string, string>) => {
    const firstInvalid = getFirstInvalidField(errors);
    if (!firstInvalid) return;

    globalThis.setTimeout(() => {
      document.querySelector<HTMLElement>(`[data-field-name="${firstInvalid}"]`)?.focus();
    }, 0);
  };

  const applyFieldErrors = (errors: Record<string, string>) => {
    setFieldErrors(errors);
    focusFirstInvalidField(errors);
  };

  const submit = async (submitMode: 'save' | 'saveAndOpenAttachments') => {
    if (isSubmitting) return;
    setError('');
    const clientErrs = validateTransactionForm(form, mode);
    if (Object.keys(clientErrs).length > 0) {
      applyFieldErrors(clientErrs);
      return;
    }
    setFieldErrors({});
    setIsSubmitting(true);
    try {
      if (mode === 'create') {
        const res = await transactionsApi.create(buildCreateTransactionPayload(form));
        const destination =
          submitMode === 'saveAndOpenAttachments'
            ? `/transactions/${res.data.id}?tab=attachments`
            : `/transactions/${res.data.id}`;
        navigate(destination);
      } else {
        await transactionsApi.update(+id!, buildUpdateTransactionPayload(form));
        navigate(`/transactions/${id}`);
      }
    } catch (err: unknown) {
      const serverFieldErrors = normalizeFieldErrors(getFieldErrors(err));
      if (Object.keys(serverFieldErrors).length > 0) {
        applyFieldErrors(serverFieldErrors);
      } else {
        setError(getApiErrorMessage(err));
      }
    } finally {
      setIsSubmitting(false);
    }
  };

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    await submit('save');
  };

  const fieldError = (name: string) => fieldErrors[name];
  const hasFieldErrors = Object.keys(fieldErrors).length > 0;
  const validationSummary = getValidationSummary(fieldErrors);
  const fieldErrorId = (name: string) => `${name}-error`;
  const fieldProps = (name: string) => ({
    'data-field-name': name,
    'aria-invalid': fieldError(name) ? true : undefined,
    'aria-describedby': fieldError(name) ? fieldErrorId(name) : undefined,
  });
  const formGroupClass = (name: string, extra = '') => {
    const errorClass = fieldError(name) ? ' has-error' : '';
    const extraClass = extra ? ` ${extra}` : '';
    return `form-group${errorClass}${extraClass}`;
  };
  const incomingSourceFieldName =
    form.incomingSourceType === 'External' ? 'incomingFromPartyId' : 'incomingFromDepartmentId';

  if (loading || referenceLoading) {
    return (
      <div className="loading">
        <LoadingInline label="جاري التحميل..." />
      </div>
    );
  }

  return (
    <div>
      <PageHeader title={header} />
      <form className="transaction-form" onSubmit={handleSubmit} noValidate>
        <TransactionValidationSummary
          hasFieldErrors={hasFieldErrors}
          validationSummary={validationSummary}
        />
        {error && !hasFieldErrors && <Alert variant="error">{error}</Alert>}

        <FormSection title="بيانات الوارد">
          <div className="transaction-form-grid transaction-form-grid--incoming">
            <div className={formGroupClass('incomingNumber', 'transaction-form-field transaction-form-field--compact')}>
              <label htmlFor="incoming-number">رقم المعاملة *</label>
              <input id="incoming-number" {...fieldProps('incomingNumber')} value={form.incomingNumber} onChange={(e) => setForm({ ...form, incomingNumber: e.target.value })} />
              {fieldError('incomingNumber') && <span id={fieldErrorId('incomingNumber')} className="field-error">{fieldError('incomingNumber')}</span>}
            </div>
            <div className={formGroupClass('incomingDate', 'transaction-form-field transaction-form-field--compact')}>
              <HijriDateInput
                id="incoming-date"
                label="تاريخ المعاملة"
                required
                value={form.incomingDate}
                onChange={(incomingDate) => setForm({ ...form, incomingDate })}
                invalid={Boolean(fieldError('incomingDate'))}
                describedBy={fieldError('incomingDate') ? fieldErrorId('incomingDate') : undefined}
                dataFieldName="incomingDate"
                disallowFutureDate
              />
              {fieldError('incomingDate') && <span id={fieldErrorId('incomingDate')} className="field-error">{fieldError('incomingDate')}</span>}
            </div>
            <IncomingSourceTypeField
              value={form.incomingSourceType}
              onChange={handleSourceTypeChange}
              error={fieldError('incomingSourceType')}
              errorId={fieldErrorId('incomingSourceType')}
              formGroupClass={formGroupClass('incomingSourceType', 'transaction-form-field transaction-form-field--compact')}
            />
            <IncomingSourceSelector
              sourceType={form.incomingSourceType}
              incomingFromPartyId={form.incomingFromPartyId}
              incomingFromDepartmentId={form.incomingFromDepartmentId}
              partyOptions={partyOptions}
              departmentOptions={departmentOptions}
              partyError={fieldError('incomingFromPartyId')}
              departmentError={fieldError('incomingFromDepartmentId')}
              partyErrorId={fieldErrorId('incomingFromPartyId')}
              departmentErrorId={fieldErrorId('incomingFromDepartmentId')}
              formGroupClass={formGroupClass(incomingSourceFieldName, 'transaction-form-field transaction-form-field--medium')}
              onPartyChange={(id) => setForm({ ...form, incomingFromPartyId: id, incomingFromDepartmentId: '' })}
              onDepartmentChange={(id) => setForm({ ...form, incomingFromDepartmentId: id, incomingFromPartyId: '' })}
            />
            <div className={formGroupClass('subject', 'transaction-form-field transaction-form-field--wide transaction-subject-field')}>
              <label htmlFor="transaction-subject">الموضوع *</label>
              <input id="transaction-subject" {...fieldProps('subject')} value={form.subject} onChange={(e) => setForm({ ...form, subject: e.target.value })} />
              {fieldError('subject') && <span id={fieldErrorId('subject')} className="field-error">{fieldError('subject')}</span>}
            </div>
            <div className={formGroupClass('categoryId', 'transaction-form-field transaction-form-field--medium')}>
              <SearchableSelect
                label="التصنيف"
                required
                value={toSelectValue(form.categoryId)}
                onChange={(id) => setForm({ ...form, categoryId: id })}
                options={categoryOptions}
                invalid={Boolean(fieldError('categoryId'))}
                describedBy={fieldError('categoryId') ? fieldErrorId('categoryId') : undefined}
                dataFieldName="categoryId"
              />
              {fieldError('categoryId') && <span id={fieldErrorId('categoryId')} className="field-error">{fieldError('categoryId')}</span>}
            </div>
            <div className={formGroupClass('priority', 'transaction-form-field transaction-form-field--compact')}>
              <label htmlFor="transaction-priority">الأولوية *</label>
              <select id="transaction-priority" {...fieldProps('priority')} value={form.priority} onChange={(e) => setForm({ ...form, priority: e.target.value })}>
                <option value="Normal">عادي</option>
                <option value="Urgent">عاجل</option>
                <option value="VeryUrgent">عاجل جداً</option>
              </select>
              {fieldError('priority') && <span id={fieldErrorId('priority')} className="field-error">{fieldError('priority')}</span>}
            </div>
            <div className={formGroupClass('responseType', 'transaction-form-field transaction-form-field--medium')}>
              <label htmlFor="transaction-response-type">نوع الإفادة *</label>
              <select id="transaction-response-type" {...fieldProps('responseType')} value={form.responseType} onChange={(e) => setForm({ ...form, responseType: e.target.value })}>
                {mode === 'edit' && form.responseType === 'None' && (
                  <option value="None">لا تتطلب إفادة</option>
                )}
                <option value="External">إفادة للجهة</option>
                <option value="Internal">إفادة داخلية</option>
                <option value="Both">إفادة للجهة وداخلية</option>
              </select>
              {fieldError('responseType') && <span id={fieldErrorId('responseType')} className="field-error">{fieldError('responseType')}</span>}
            </div>
            <div className={formGroupClass('responseDueDays', 'transaction-form-field transaction-form-field--compact')}>
              <label htmlFor="transaction-response-due-days">عدد الأيام للرد *</label>
              <input id="transaction-response-due-days" {...fieldProps('responseDueDays')} type="number" min="1" value={form.responseDueDays}
                onChange={(e) => setForm({ ...form, responseDueDays: e.target.value })} />
              {fieldError('responseDueDays') && <span id={fieldErrorId('responseDueDays')} className="field-error">{fieldError('responseDueDays')}</span>}
              {computedResponseDueDate && (
                <small className="text-muted">
                  تاريخ الرد المطلوب: {formatHijri(computedResponseDueDate)}
                </small>
              )}
            </div>
            {mode === 'edit' && (
              <p className="text-muted transaction-edit-response-hint">لتسجيل الإفادة استخدم إجراء «تسجيل الإفادة» من صفحة تفاصيل المعاملة.</p>
            )}
          </div>
        </FormSection>

        <FormSection
          title="بيانات التوجيه والإرسال الداخلي"
          description={OUTGOING_HINT}
        >
          <div className="transaction-form-grid transaction-form-grid--routing">
            <div className={formGroupClass('outgoingDepartmentIds', 'transaction-form-field transaction-form-field--wide')}>
              <MultiSelect
                label="الإدارات الموجه لها"
                options={departments.map((d) => ({ id: d.id, name: d.name, isActive: d.isActive }))}
                selected={form.outgoingDepartmentIds}
                onChange={(ids) => setForm({ ...form, outgoingDepartmentIds: ids })}
                invalid={Boolean(fieldError('outgoingDepartmentIds'))}
                describedBy={fieldError('outgoingDepartmentIds') ? fieldErrorId('outgoingDepartmentIds') : undefined}
                dataFieldName="outgoingDepartmentIds"
              />
              {fieldError('outgoingDepartmentIds') && <span id={fieldErrorId('outgoingDepartmentIds')} className="field-error">{fieldError('outgoingDepartmentIds')}</span>}
            </div>
            <div className={formGroupClass('outgoingNumber', 'transaction-form-field transaction-form-field--compact')}>
              <label htmlFor="outgoing-number">رقم خطاب الإحالة للإدارة</label>
              <input id="outgoing-number" {...fieldProps('outgoingNumber')} value={form.outgoingNumber} onChange={(e) => setForm({ ...form, outgoingNumber: e.target.value })} />
              {fieldError('outgoingNumber') && <span id={fieldErrorId('outgoingNumber')} className="field-error">{fieldError('outgoingNumber')}</span>}
            </div>
            <div className={formGroupClass('outgoingDate', 'transaction-form-field transaction-form-field--compact')}>
              <HijriDateInput
                id="outgoing-date"
                label="تاريخ الإحالة"
                value={form.outgoingDate}
                onChange={(outgoingDate) => setForm({ ...form, outgoingDate })}
                invalid={Boolean(fieldError('outgoingDate'))}
                describedBy={fieldError('outgoingDate') ? fieldErrorId('outgoingDate') : undefined}
                dataFieldName="outgoingDate"
                disallowFutureDate
              />
              {fieldError('outgoingDate') && <span id={fieldErrorId('outgoingDate')} className="field-error">{fieldError('outgoingDate')}</span>}
            </div>
          </div>
        </FormSection>

        <FormSection title="ملاحظات">
          <div className="transaction-form-grid">
            <div className="form-group transaction-form-field transaction-form-field--wide">
              <textarea rows={3} value={form.notes} onChange={(e) => setForm({ ...form, notes: e.target.value })} aria-label="ملاحظات" />
            </div>
          </div>
        </FormSection>

        <TransactionFormActions
          mode={mode}
          isSubmitting={isSubmitting}
          onSaveAndOpenAttachments={() => submit('saveAndOpenAttachments')}
          onCancel={() => navigate(-1)}
        />
      </form>
    </div>
  );
}
