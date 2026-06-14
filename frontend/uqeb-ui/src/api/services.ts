import api from './client';
import type {
  LoginResponse, TransactionListItem, TransactionDetail, PagedResult,
  DashboardSummary, Department, ExternalParty, User, FollowUp, Assignment, Category,
  ReportTransactionRow, ReportSectionCounts
} from './types';

export const authApi = {
  login: (username: string, password: string) =>
    api.post<LoginResponse>('/auth/login', { username, password }),
};

export const transactionsApi = {
  search: (params: Record<string, unknown>) =>
    api.get<PagedResult<TransactionListItem>>('/transactions', { params }),
  getById: (id: number) => api.get<TransactionDetail>(`/transactions/${id}`),
  getBasic: (id: number) => api.get<TransactionDetail>(`/transactions/${id}/basic`),
  getAssignments: (id: number) => api.get<Assignment[]>(`/transactions/${id}/assignments`),
  getFollowUps: (id: number) => api.get<FollowUp[]>(`/transactions/${id}/followups`),
  getAttachments: (id: number) => api.get<import('./types').Attachment[]>(`/transactions/${id}/attachments`),
  getAuditLog: (id: number, page = 1, pageSize = 50) =>
    api.get<PagedResult<import('./types').AuditLog>>(`/transactions/${id}/audit-log`, { params: { page, pageSize } }),
  create: (data: Record<string, unknown>) => api.post<TransactionDetail>('/transactions', data),
  update: (id: number, data: Record<string, unknown>) => api.put<TransactionDetail>(`/transactions/${id}`, data),
  close: (id: number) => api.post(`/transactions/${id}/close`),
  completeResponse: (id: number, data: Record<string, unknown>) =>
    api.post<TransactionDetail>(`/transactions/${id}/complete-response`, data),
  cancel: (id: number) => api.post(`/transactions/${id}/cancel`),
  archive: (id: number) => api.post(`/transactions/${id}/archive`),
  getFollowUpDepartments: (id: number) =>
    api.get<import('./types').FollowUpDepartmentOption[]>(`/transactions/${id}/followup-departments`),
  addFollowUp: (id: number, data: Record<string, unknown>) =>
    api.post<FollowUp>(`/transactions/${id}/followups`, data),
  replyFollowUp: (id: number, followUpId: number, data: Record<string, unknown>) =>
    api.post(`/transactions/${id}/followups/${followUpId}/reply`, data),
  addAssignment: (id: number, data: Record<string, unknown>) =>
    api.post<Assignment>(`/transactions/${id}/assignments`, data),
  replyAssignment: (id: number, assignmentId: number, data: Record<string, unknown>) =>
    api.post(`/transactions/${id}/assignments/${assignmentId}/reply`, data),
  uploadAttachment: (id: number, file: File, attachmentType?: string) => {
    const form = new FormData();
    form.append('file', file);
    if (attachmentType) form.append('attachmentType', attachmentType);
    return api.post(`/transactions/${id}/attachments`, form, {
      headers: { 'Content-Type': 'multipart/form-data' },
    });
  },
  downloadAttachment: (id: number, attachmentId: number) =>
    api.get(`/transactions/${id}/attachments/${attachmentId}/download`, { responseType: 'blob' }),
};

export const dashboardApi = {
  summary: () => api.get<DashboardSummary>('/dashboard/summary'),
  actionRequired: () => api.get<import('./types').TransactionListItem[]>('/dashboard/action-required'),
  topOverdueDepartments: () => api.get<import('./types').DepartmentOverdue[]>('/dashboard/top-overdue-departments'),
  topIncomingParties: () => api.get<import('./types').ExternalPartyReport[]>('/dashboard/top-incoming-parties'),
  categoryDistribution: () => api.get<import('./types').CategoryDistribution[]>('/dashboard/category-distribution'),
  statusDistribution: () => api.get<import('./types').StatusDistribution[]>('/dashboard/status-distribution'),
};

export const reportsApi = {
  dashboard: () => api.get('/reports/dashboard'),
  pageSummary: (params?: Record<string, unknown>, config?: { signal?: AbortSignal }) =>
    api.get<ReportSectionCounts>('/reports/page-summary', { params, ...config }),
  overdue: (params?: Record<string, unknown>) => api.get<TransactionListItem[]>('/reports/overdue', { params }),
  open: (params?: Record<string, unknown>) => api.get<TransactionListItem[]>('/reports/open', { params }),
  waitingReplies: (params?: Record<string, unknown>) => api.get<TransactionListItem[]>('/reports/waiting-replies', { params }),
  pendingResponse: (params?: Record<string, unknown>) => api.get<TransactionListItem[]>('/reports/pending-response', { params }),
  responseRequired: (params?: Record<string, unknown>) => api.get<TransactionListItem[]>('/reports/response-required', { params }),
  overdueResponses: (params?: Record<string, unknown>) => api.get<TransactionListItem[]>('/reports/overdue-responses', { params }),
  pendingAssignments: (params?: Record<string, unknown>) => api.get<TransactionListItem[]>('/reports/pending-assignments', { params }),
  partialReplies: (params?: Record<string, unknown>) => api.get<TransactionListItem[]>('/reports/partial-replies', { params }),
  byDepartment: (params?: Record<string, unknown>) => api.get('/reports/by-department', { params }),
  byExternalParty: (params?: Record<string, unknown>) => api.get('/reports/by-external-party', { params }),
  byCategory: (params?: Record<string, unknown>) => api.get('/reports/by-category', { params }),
  byIncomingParty: (params?: Record<string, unknown>) => api.get('/reports/by-incoming-party', { params }),
  byOutgoingParty: (params?: Record<string, unknown>) => api.get('/reports/by-outgoing-party', { params }),
  byOutgoingDepartment: (params?: Record<string, unknown>) => api.get('/reports/by-outgoing-department', { params }),
  departmentSummary: (params?: Record<string, unknown>) => api.get('/reports/department-summary', { params }),
  departmentIncomingClosed: (params?: Record<string, unknown>) => api.get('/reports/department-incoming-closed', { params }),
  exportDepartmentIncomingClosedExcel: (params?: Record<string, unknown>) =>
    api.get('/reports/department-incoming-closed/export-excel', { params, responseType: 'blob' }),
  exportDepartmentIncomingClosedPdf: (params?: Record<string, unknown>) =>
    api.get('/reports/department-incoming-closed/export-pdf', { params, responseType: 'blob' }),
  monthly: (year: number) => api.get('/reports/monthly', { params: { year } }),
  exportExcel: (reportType: string, params?: Record<string, unknown>) =>
    api.get(`/reports/export/${reportType}`, { params, responseType: 'blob' }),
  responseRequiredDetails: (params?: Record<string, unknown>, config?: { signal?: AbortSignal }) =>
    api.get<PagedResult<ReportTransactionRow>>('/reports/response-required/details', { params, ...config }),
  overdueResponsesDetails: (params?: Record<string, unknown>, config?: { signal?: AbortSignal }) =>
    api.get<PagedResult<ReportTransactionRow>>('/reports/overdue-responses/details', { params, ...config }),
  openAssignmentsDetails: (params?: Record<string, unknown>, config?: { signal?: AbortSignal }) =>
    api.get<PagedResult<ReportTransactionRow>>('/reports/open-assignments/details', { params, ...config }),
  partialRepliesDetails: (params?: Record<string, unknown>, config?: { signal?: AbortSignal }) =>
    api.get<PagedResult<ReportTransactionRow>>('/reports/partial-replies/details', { params, ...config }),
  overdueDetails: (params?: Record<string, unknown>, config?: { signal?: AbortSignal }) =>
    api.get<PagedResult<ReportTransactionRow>>('/reports/overdue/details', { params, ...config }),
  waitingReplyDetails: (params?: Record<string, unknown>, config?: { signal?: AbortSignal }) =>
    api.get<PagedResult<ReportTransactionRow>>('/reports/waiting-reply/details', { params, ...config }),
  openDetails: (params?: Record<string, unknown>, config?: { signal?: AbortSignal }) =>
    api.get<PagedResult<ReportTransactionRow>>('/reports/open/details', { params, ...config }),
};

export const categoriesApi = {
  getAll: (activeOnly = true) => api.get<Category[]>('/categories', { params: { activeOnly } }),
  create: (data: Record<string, unknown>) => api.post<Category>('/categories', data),
  update: (id: number, data: Record<string, unknown>) => api.put<Category>(`/categories/${id}`, data),
};

export const departmentsApi = {
  getAll: (activeOnly = true) => api.get<Department[]>('/departments', { params: { activeOnly } }),
  create: (data: Record<string, unknown>) => api.post<Department>('/departments', data),
  update: (id: number, data: Record<string, unknown>) => api.put<Department>(`/departments/${id}`, data),
};

export const externalPartiesApi = {
  getAll: (activeOnly = true) => api.get<ExternalParty[]>('/external-parties', { params: { activeOnly } }),
  create: (data: Record<string, unknown>) => api.post<ExternalParty>('/external-parties', data),
  update: (id: number, data: Record<string, unknown>) => api.put<ExternalParty>(`/external-parties/${id}`, data),
};

export const usersApi = {
  getAll: () => api.get<User[]>('/users'),
  create: (data: Record<string, unknown>) => api.post<User>('/users', data),
  update: (id: number, data: Record<string, unknown>) => api.put<User>(`/users/${id}`, data),
};
