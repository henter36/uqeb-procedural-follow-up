import api from './client';
import type {
  LoginResponse, TransactionListItem, TransactionDetail, PagedResult,
  DashboardSummary, Department, ExternalParty, User, FollowUp, Assignment, Category,
  ReportTransactionRow, ReportSectionCounts, LetterTemplate, FollowUpLetterPreview,
  TransactionWorkspace, LetterTemplateVariable, LetterTemplateValidationResult,
  CreateLetterTemplateRequest, UpdateLetterTemplateRequest, LetterTemplateType,
  LetterTemplatePreviewResponse, FollowUpPrintJobListStatusFilter,
  FollowUpPrintFilter, PagedEligibleTransactions, FollowUpPrintEligibilityPreview,
  CreateFollowUpPrintJobRequest, FollowUpPrintJob, PagedFollowUpPrintJobs,
  FollowUpLetterPrintRecord, FollowUpPrintPendingSummary, FollowUpPrintRecordPrintView, UserNotification,
  CreateDirectPrintRequest,
  DepartmentResponseDto, DepartmentResponseSummaryDto, DepartmentResponseAttachmentDto, DepartmentTransactionResponseItemDto,
  DepartmentResponseStatsDto, SystemVersionInfo,
} from './types';
import type {
  InstitutionalReportManifest,
  ReportExportRequest,
  ReportBuildRequest,
  ReportTemplate,
  SaveReportTemplateRequest,
} from './institutionalReports.types';

export const authApi = {
  login: (username: string, password: string) =>
    api.post<LoginResponse>('/auth/login', { username, password }),
};

export const systemApi = {
  getVersion: () => api.get<SystemVersionInfo>('/system/version'),
};

export const transactionsApi = {
  search: (params: Record<string, unknown>) =>
    api.get<PagedResult<TransactionListItem>>('/transactions', { params }),
  getById: (id: number) => api.get<TransactionDetail>(`/transactions/${id}`),
  getBasic: (id: number) => api.get<TransactionDetail>(`/transactions/${id}/basic`),
  getWorkspace: (id: number, config?: { signal?: AbortSignal }) =>
    api.get<TransactionWorkspace>(`/transactions/${id}/workspace`, config),
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
  previewFollowUpLetter: (id: number, data?: { targetEntity?: string; content?: string }) =>
    api.post<FollowUpLetterPreview>(`/transactions/${id}/follow-up-letter/preview`, data ?? {}),
  downloadFollowUpLetterPdf: (id: number, data: { targetEntity?: string; content: string }) =>
    api.post(`/transactions/${id}/follow-up-letter/pdf`, data, { responseType: 'blob' }),
  previewExcelImport: (file: File) => {
    const form = new FormData();
    form.append('file', file);
    return api.post<import('./types').ExcelImportPreviewResult>('/transactions/import/excel/preview', form);
  },
  commitExcelImport: (file: File) => {
    const form = new FormData();
    form.append('file', file);
    return api.post<import('./types').ExcelImportCommitResult>('/transactions/import/excel/commit', form);
  },
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
  dashboard: (config?: { signal?: AbortSignal }) =>
    api.get<DashboardSummary>('/reports/dashboard', config),
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
  search: (params: import('./types').ReferenceDataListParams) =>
    api.get<PagedResult<Category>>('/categories', { params }),
  lookup: (search?: string, activeOnly = true, limit = 50) =>
    api.get<import('./types').LookupItem[]>('/categories/lookup', { params: { search, activeOnly, limit } }),
  getById: (id: number) => api.get<Category>(`/categories/${id}`),
  create: (data: Record<string, unknown>) => api.post<Category>('/categories', data),
  update: (id: number, data: Record<string, unknown>) => api.put<Category>(`/categories/${id}`, data),
};

export const departmentsApi = {
  getAll: (activeOnly = true) => api.get<Department[]>('/departments', { params: { activeOnly } }),
  search: (params: import('./types').ReferenceDataListParams) =>
    api.get<PagedResult<Department>>('/departments', { params }),
  lookup: (search?: string, activeOnly = true, limit = 50) =>
    api.get<import('./types').LookupItem[]>('/departments/lookup', { params: { search, activeOnly, limit } }),
  getById: (id: number) => api.get<Department>(`/departments/${id}`),
  create: (data: Record<string, unknown>) => api.post<Department>('/departments', data),
  update: (id: number, data: Record<string, unknown>) => api.put<Department>(`/departments/${id}`, data),
};

export const externalPartiesApi = {
  getAll: (activeOnly = true) => api.get<ExternalParty[]>('/external-parties', { params: { activeOnly } }),
  search: (params: import('./types').ReferenceDataListParams) =>
    api.get<PagedResult<ExternalParty>>('/external-parties', { params }),
  lookup: (search?: string, activeOnly = true, limit = 50) =>
    api.get<import('./types').LookupItem[]>('/external-parties/lookup', { params: { search, activeOnly, limit } }),
  getById: (id: number) => api.get<ExternalParty>(`/external-parties/${id}`),
  create: (data: Record<string, unknown>) => api.post<ExternalParty>('/external-parties', data),
  update: (id: number, data: Record<string, unknown>) => api.put<ExternalParty>(`/external-parties/${id}`, data),
};

export const usersApi = {
  getAll: () => api.get<User[]>('/users'),
  search: (params: import('./types').ReferenceDataListParams) =>
    api.get<PagedResult<User>>('/users', { params }),
  getById: (id: number) => api.get<User>(`/users/${id}`),
  create: (data: Record<string, unknown>) => api.post<User>('/users', data),
  update: (id: number, data: Record<string, unknown>) => api.put<User>(`/users/${id}`, data),
  resetPassword: (id: number, newPassword: string) =>
    api.post(`/users/${id}/reset-password`, { newPassword }),
};

export const letterTemplatesApi = {
  getFollowUp: () => api.get<LetterTemplate>('/letter-templates/follow-up'),
  updateFollowUp: (content: string) => api.put<LetterTemplate>('/letter-templates/follow-up', { content }),
  list: (params?: { type?: LetterTemplateType; active?: boolean; search?: string }) =>
    api.get<LetterTemplate[]>('/letter-templates', { params }),
  getById: (id: number) => api.get<LetterTemplate>(`/letter-templates/${id}`),
  create: (data: CreateLetterTemplateRequest) => api.post<LetterTemplate>('/letter-templates', data),
  update: (id: number, data: UpdateLetterTemplateRequest) =>
    api.put<LetterTemplate>(`/letter-templates/${id}`, data),
  copy: (id: number) => api.post<LetterTemplate>(`/letter-templates/${id}/copy`),
  setDefault: (id: number) => api.post<LetterTemplate>(`/letter-templates/${id}/set-default`),
  activate: (id: number) => api.patch<LetterTemplate>(`/letter-templates/${id}/activate`),
  deactivate: (id: number) => api.patch<LetterTemplate>(`/letter-templates/${id}/deactivate`),
  delete: (id: number, replacementDefaultId?: number) =>
    api.delete(`/letter-templates/${id}`, { params: { replacementDefaultId } }),
  validate: (content: string) =>
    api.post<LetterTemplateValidationResult>('/letter-templates/validate', { content }),
  preview: (data: CreateLetterTemplateRequest) =>
    api.post<LetterTemplatePreviewResponse>('/letter-templates/preview', data),
  getVariables: () => api.get<LetterTemplateVariable[]>('/letter-templates/variables'),
};

export const followUpPrintApi = {
  getEligible: (params?: FollowUpPrintFilter) =>
    api.get<PagedEligibleTransactions>('/follow-up-print/eligible', { params }),
  getPendingSummary: () => api.get<FollowUpPrintPendingSummary>('/follow-up-print/pending-summary'),
  getPending: (params?: { page?: number; pageSize?: number }) =>
    api.get<FollowUpLetterPrintRecord[]>('/follow-up-print/pending', { params }),
  previewJob: (data: CreateFollowUpPrintJobRequest) =>
    api.post<FollowUpPrintEligibilityPreview>('/follow-up-print/jobs/preview', data),
  createJob: (data: CreateFollowUpPrintJobRequest) =>
    api.post<FollowUpPrintJob>('/follow-up-print/jobs', data),
  listJobs: (params?: { page?: number; pageSize?: number; status?: FollowUpPrintJobListStatusFilter }) =>
    api.get<PagedFollowUpPrintJobs>('/follow-up-print/jobs', { params }),
  getJob: (id: number) => api.get<FollowUpPrintJob>(`/follow-up-print/jobs/${id}`),
  cancelJob: (id: number) => api.post<FollowUpPrintJob>(`/follow-up-print/jobs/${id}/cancel`),
  retryJob: (id: number) => api.post<FollowUpPrintJob>(`/follow-up-print/jobs/${id}/retry`),
  getPartPrintView: (jobId: number, partNumber: number) =>
    api.get<string>(`/follow-up-print/jobs/${jobId}/parts/${partNumber}/print-view`, {
      responseType: 'text',
      headers: { Accept: 'text/html' },
    }),
  markPartPrintRequested: (jobId: number, partNumber: number) =>
    api.post<FollowUpLetterPrintRecord[]>(
      `/follow-up-print/jobs/${jobId}/parts/${partNumber}/mark-print-requested`,
    ),
  getTransactionPrintView: (
    transactionId: number,
    data?: { targetEntity?: string; content?: string; templateId?: number },
  ) =>
    api.post<string>(`/follow-up-print/transactions/${transactionId}/print-view`, data ?? {}, {
      responseType: 'text',
      headers: { Accept: 'text/html' },
    }),
  registerDirectPrintRequest: (transactionId: number, data: CreateDirectPrintRequest) =>
    api.post<FollowUpLetterPrintRecord>(`/follow-up-print/transactions/${transactionId}/print-requests`, data),
  confirmRecord: (id: number) => api.post<FollowUpLetterPrintRecord>(`/follow-up-print/records/${id}/confirm`),
  getRecordPrintView: (id: number) =>
    api.get<FollowUpPrintRecordPrintView>(`/follow-up-print/records/${id}/print-view`),
  cancelRecord: (id: number, reason: string) =>
    api.post<FollowUpLetterPrintRecord>(`/follow-up-print/records/${id}/cancel`, { reason }),
  linkFollowUp: (id: number, followUpId: number) =>
    api.post<FollowUpLetterPrintRecord>(`/follow-up-print/records/${id}/link-follow-up`, { followUpId }),
  reprintRecord: (id: number, idempotencyKey?: string) =>
    api.post<FollowUpLetterPrintRecord>(`/follow-up-print/records/${id}/reprint`, { idempotencyKey }),
};

export const notificationsApi = {
  list: (params?: { unreadOnly?: boolean; since?: string }) =>
    api.get<UserNotification[]>('/notifications', { params }),
  markRead: (id: number) => api.post<UserNotification>(`/notifications/${id}/read`),
};

export const securityApi = {
  getLoginAttempts: (params?: Record<string, unknown>) =>
    api.get<import('./types').LoginAttemptsPage>('/security/login-attempts', { params }),
  getAlerts: (params?: Record<string, unknown>) =>
    api.get<import('./types').SecurityAlertsSummary>('/security/alerts', { params }),
  markAlertRead: (id: number) => api.post(`/security/alerts/${id}/read`),
  markAllAlertsRead: () => api.post<{ marked: number }>('/security/alerts/mark-all-read'),
};

export type {
  InstitutionalReportManifest,
  InstitutionalReportPage,
  ReportExportRequest,
  ReportBuildRequest,
  ReportTemplate,
  SaveReportTemplateRequest,
} from './institutionalReports.types';

export const institutionalReportsApi = {
  preview: (payload: ReportBuildRequest, signal?: AbortSignal) =>
    api.post<InstitutionalReportManifest>('/institutional-reports/preview', payload, { signal }),
  export: (payload: ReportExportRequest) =>
    api.post('/institutional-reports/export', payload, { responseType: 'blob' }),
  getTemplates: () => api.get<ReportTemplate[]>('/institutional-reports/templates'),
  saveTemplate: (payload: SaveReportTemplateRequest) =>
    api.post<ReportTemplate>('/institutional-reports/templates', payload),
  deleteTemplate: (id: number) => api.delete(`/institutional-reports/templates/${id}`),
};

export const departmentResponsesApi = {
  getDepartmentTransactions: () => api.get<DepartmentTransactionResponseItemDto[]>('/department-responses/department-transactions'),
  getMyResponses: () => api.get<DepartmentResponseSummaryDto[]>('/department-responses/my'),
  getMyStats: () => api.get<DepartmentResponseStatsDto>('/department-responses/my-stats'),
  getPendingReview: () => api.get<DepartmentResponseSummaryDto[]>('/department-responses/pending-review'),
  getById: (id: number) => api.get<DepartmentResponseDto>(`/department-responses/${id}`),
  create: (data: { transactionId: number; responseText: string }) =>
    api.post<DepartmentResponseDto>('/department-responses', data),
  update: (id: number, data: { responseText: string }) =>
    api.put<DepartmentResponseDto>(`/department-responses/${id}`, data),
  submit: (id: number) => api.post<DepartmentResponseDto>(`/department-responses/${id}/submit`),
  approve: (id: number) => api.post<DepartmentResponseDto>(`/department-responses/${id}/approve`),
  returnForCorrection: (id: number, reviewNote: string) =>
    api.post<DepartmentResponseDto>(`/department-responses/${id}/return`, { reviewNote }),
  reject: (id: number, reviewNote: string) =>
    api.post<DepartmentResponseDto>(`/department-responses/${id}/reject`, { reviewNote }),
  uploadAttachment: (id: number, file: File) => {
    const form = new FormData();
    form.append('file', file);
    return api.post<DepartmentResponseAttachmentDto>(`/department-responses/${id}/attachments`, form);
  },
  deleteAttachment: (id: number, attachmentId: number) =>
    api.delete(`/department-responses/${id}/attachments/${attachmentId}`),
  downloadAttachment: (id: number, attachmentId: number) =>
    api.get(`/department-responses/${id}/attachments/${attachmentId}/download`, { responseType: 'blob' }),
};
