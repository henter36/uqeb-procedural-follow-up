import axios from 'axios';

export function toIsoDate(date: string | null | undefined): string | null {
  if (!date || !date.trim()) return null;
  return date.includes('T') ? date : `${date}T00:00:00`;
}

export function toNullableNumber(value: string | number | null | undefined): number | null {
  if (value === '' || value === null || value === undefined) return null;
  const n = Number(value);
  return Number.isNaN(n) ? null : n;
}

export function toNullableString(value: string | null | undefined): string | null {
  if (value === null || value === undefined || value.trim() === '') return null;
  return value.trim();
}

type TransactionFormPayload = {
  incomingNumber: string;
  incomingDate: string;
  subject: string;
  incomingSourceType: string;
  incomingFromPartyId: string | number | null;
  incomingFromDepartmentId: string | number | null;
  outgoingNumber: string;
  outgoingDate: string;
  outgoingDepartmentIds: number[];
  responseType: string;
  responseDueDays: string | number | null;
  priority: string;
  categoryId: string | number | null;
  notes: string;
};

function buildIncomingFields(form: Pick<TransactionFormPayload, 'incomingSourceType' | 'incomingFromPartyId' | 'incomingFromDepartmentId'>) {
  const isExternal = form.incomingSourceType === 'External';
  return {
    incomingSourceType: form.incomingSourceType,
    incomingFromPartyId: isExternal ? toNullableNumber(form.incomingFromPartyId) : null,
    incomingFromDepartmentId: !isExternal ? toNullableNumber(form.incomingFromDepartmentId) : null,
  };
}

export function buildCreateTransactionPayload(form: TransactionFormPayload) {
  const hasOutgoing = Boolean(
    form.outgoingNumber.trim() || form.outgoingDate || form.outgoingDepartmentIds.length > 0
  );
  return {
    incomingNumber: form.incomingNumber.trim(),
    incomingDate: toIsoDate(form.incomingDate),
    subject: form.subject.trim(),
    ...buildIncomingFields(form),
    outgoingNumber: hasOutgoing ? toNullableString(form.outgoingNumber) : null,
    outgoingDate: hasOutgoing ? toIsoDate(form.outgoingDate) : null,
    outgoingDepartmentIds: hasOutgoing ? form.outgoingDepartmentIds : [],
    responseType: form.responseType || 'External',
    responseDueDays: toNullableNumber(form.responseDueDays),
    priority: form.priority,
    categoryId: toNullableNumber(form.categoryId),
    notes: toNullableString(form.notes),
  };
}

export function buildUpdateTransactionPayload(form: TransactionFormPayload) {
  const base = buildCreateTransactionPayload(form);
  const responseType = form.responseType || 'External';
  return {
    ...base,
    requiresResponse: responseType !== 'None',
  };
}

export function buildCompleteResponsePayload(form: {
  responseDate: string;
  responseSummary: string;
  outgoingNumber: string;
  outgoingDate: string;
  requiresOutgoing: boolean;
}) {
  const payload: Record<string, unknown> = {
    responseDate: toIsoDate(form.responseDate),
    responseSummary: form.responseSummary.trim(),
  };
  if (form.requiresOutgoing) {
    payload.outgoingNumber = form.outgoingNumber.trim();
    payload.outgoingDate = toIsoDate(form.outgoingDate);
  }
  return payload;
}

export function buildCreateAssignmentPayload(form: {
  departmentId: string | number;
  assignedDate: string;
  requiredAction: string;
  replyDueDays: string | number | null;
  dueDate: string;
}) {
  const departmentId = Number(form.departmentId);
  if (!departmentId) throw new Error('يجب اختيار الإدارة');
  return {
    departmentId,
    assignedDate: toIsoDate(form.assignedDate),
    requiredAction: toNullableString(form.requiredAction),
    replyDueDays: toNullableNumber(form.replyDueDays),
    dueDate: toIsoDate(form.dueDate),
  };
}

export function buildCreateFollowUpPayload(form: {
  followUpNumber: string;
  followUpDate: string;
  departmentIds: number[];
  notes: string;
}) {
  return {
    followUpNumber: toNullableString(form.followUpNumber),
    followUpDate: toIsoDate(form.followUpDate),
    departmentIds: form.departmentIds,
    notes: toNullableString(form.notes),
  };
}

export function buildReplyPayload(form: { replyDate: string; replySummary: string }) {
  return {
    replyDate: toIsoDate(form.replyDate),
    replySummary: form.replySummary.trim(),
  };
}

export function getFieldErrors(err: unknown): Record<string, string> {
  if (!axios.isAxiosError(err)) return {};
  const data = err.response?.data as { errors?: Record<string, string> } | undefined;
  return data?.errors ?? {};
}

export type ApiErrorDetails = {
  message: string;
  title: string;
  detail: string;
  errorCode: string;
  correlationId: string;
  validationErrors: Record<string, string>;
  httpStatus: number | null;
};

function asRecord(value: unknown): Record<string, unknown> | null {
  if (!value || typeof value !== 'object' || Array.isArray(value))
    return null;

  return value as Record<string, unknown>;
}

function readString(record: Record<string, unknown>, key: string): string {
  const value = record[key];
  return typeof value === 'string' ? value : '';
}

function readValidationErrors(record: Record<string, unknown>): Record<string, string> {
  const errors = record.errors;
  if (!errors || typeof errors !== 'object' || Array.isArray(errors))
    return {};

  return Object.fromEntries(
    Object.entries(errors as Record<string, unknown>)
      .map(([key, value]) => {
        if (Array.isArray(value))
          return [key, value.filter((item): item is string => typeof item === 'string').join(' — ')];

        return [key, typeof value === 'string' ? value : ''];
      })
      .filter(([, message]) => message.length > 0),
  );
}

export function getApiErrorDetails(err: unknown): ApiErrorDetails {
  const empty: ApiErrorDetails = {
    message: '',
    title: '',
    detail: '',
    errorCode: '',
    correlationId: '',
    validationErrors: {},
    httpStatus: null,
  };

  if (!axios.isAxiosError(err)) {
    return {
      ...empty,
      message: err instanceof Error ? err.message : 'حدث خطأ غير متوقع',
    };
  }

  const httpStatus = err.response?.status ?? null;
  const headerCorrelationId = err.response?.headers?.['x-correlation-id'];
  const responseRecord = asRecord(err.response?.data);
  const validationErrors = responseRecord ? readValidationErrors(responseRecord) : {};
  const validationMessage = Object.values(validationErrors).filter(Boolean).join(' — ');

  if (!responseRecord) {
    return {
      ...empty,
      httpStatus,
      message: err.message || 'حدث خطأ في الاتصال',
      correlationId: typeof headerCorrelationId === 'string' ? headerCorrelationId : '',
    };
  }

  const message = readString(responseRecord, 'message')
    || validationMessage
    || readString(responseRecord, 'title')
    || readString(responseRecord, 'detail')
    || err.message
    || 'حدث خطأ';

  return {
    message,
    title: readString(responseRecord, 'title'),
    detail: readString(responseRecord, 'detail'),
    errorCode: readString(responseRecord, 'errorCode'),
    correlationId: readString(responseRecord, 'correlationId')
      || (typeof headerCorrelationId === 'string' ? headerCorrelationId : ''),
    validationErrors,
    httpStatus,
  };
}

export function getApiErrorMessage(err: unknown): string {
  return getApiErrorDetails(err).message;
}
