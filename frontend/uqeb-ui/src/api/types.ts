export interface LoginResponse {
  token: string;
  username: string;
  fullName: string;
  role: string;
  departmentId?: number;
  departmentName?: string;
}

export interface Category {
  id: number;
  name: string;
  code?: string;
  isActive: boolean;
  createdAt?: string;
}

export interface OutgoingParty {
  id: number;
  externalPartyId: number;
  partyName: string;
}

export interface OutgoingDepartment {
  id: number;
  departmentId: number;
  departmentName: string;
}

export interface DepartmentSummaryReport {
  departmentId: number;
  departmentName: string;
  totalIncoming: number;
  openCount: number;
  waitingForReplyCount: number;
  overdueCount: number;
  closedCount: number;
  closeRate: number;
}

export interface OutgoingDepartmentReport {
  departmentId: number;
  departmentName: string;
  transactionCount: number;
  openCount: number;
  closedCount: number;
  overdueCount: number;
}

export interface FollowUpRecipient {
  id: number;
  externalPartyId: number;
  partyName: string;
}

export interface FollowUpDepartment {
  id: number;
  departmentId: number;
  departmentName: string;
}

export interface FollowUpDepartmentOption {
  departmentId: number;
  departmentName: string;
  isDefaultSelected: boolean;
  source: string;
}

export interface TransactionListItem {
  id: number;
  internalTrackingNumber: string;
  incomingNumber: string;
  incomingDate: string;
  subject: string;
  incomingFrom?: string;
  incomingSourceType: string;
  outgoingNumber?: string;
  outgoingDate?: string;
  outgoingPartyNames: string[];
  outgoingDepartmentNames: string[];
  status: string;
  priority: string;
  categoryName?: string;
  requiresResponse: boolean;
  responseCompleted: boolean;
  responseDays?: number;
  responseDueDate?: string;
  daysRemainingForResponse?: number;
  daysSinceIncoming?: number;
  daysSinceLastFollowUp?: number | null;
  lastFollowUpDate?: string;
  responseTimingStatus?: string;
  responseTimingLabel?: string;
  isOverdue: boolean;
  isResponseOverdue: boolean;
  hasPendingAssignments: boolean;
  isArchived: boolean;
  createdByName: string;
  createdAt: string;
}

export interface TransactionDetail extends TransactionListItem {
  outgoingTo?: string;
  incomingFromPartyId?: number;
  incomingFromDepartmentId?: number;
  categoryId?: number;
  responseType: string;
  responseDueDays?: number;
  responseCompletedDate?: string;
  responseSummary?: string;
  category?: string;
  notes?: string;
  updatedAt?: string;
  outgoingParties: OutgoingParty[];
  outgoingDepartments: OutgoingDepartment[];
  repliedDepartmentNames: string[];
  pendingDepartmentNames: string[];
  followUps: FollowUp[];
  assignments: Assignment[];
  attachments: Attachment[];
  auditLogs: AuditLog[];
}

export interface FollowUp {
  id: number;
  followUpNumber?: string;
  followUpDate: string;
  sentTo?: string;
  recipients: FollowUpRecipient[];
  departments: FollowUpDepartment[];
  notes?: string;
  requiresReply: boolean;
  replyStatus: string;
  replyDate?: string;
  replySummary?: string;
  createdByName: string;
  createdAt: string;
}

export interface Assignment {
  id: number;
  departmentId: number;
  departmentName: string;
  assignedDate: string;
  requiredAction?: string;
  requiresReply: boolean;
  replyDueDays?: number;
  dueDate?: string;
  replyStatus: string;
  replyDate?: string;
  replySummary?: string;
  status: string;
  isOverdue: boolean;
  createdByName: string;
  createdAt: string;
}

export interface Attachment {
  id: number;
  attachmentType?: string;
  originalFileName: string;
  contentType?: string;
  fileSize: number;
  uploadedByName: string;
  uploadedAt: string;
}

export interface AuditLog {
  id: number;
  action: string;
  entityName?: string;
  entityId?: number;
  oldValue?: string;
  newValue?: string;
  userName: string;
  createdAt: string;
}

export interface TransactionTemporalFacts {
  isOpen: boolean;
  isResponseOverdue: boolean;
  isOverdue: boolean;
  ageDays: number;
  daysOverdue?: number | null;
  completionDays?: number | null;
}

export interface TransactionWorkspaceAllowedActions {
  canEdit: boolean;
  canClose: boolean;
  isDepartmentUser: boolean;
  canRegisterResponse: boolean;
  canShowClose: boolean;
  showMutationActions: boolean;
  canReply: boolean;
  hasPendingDepartments: boolean;
}

export interface TransactionWorkspace {
  transaction: TransactionDetail;
  assignments: Assignment[];
  followUps: FollowUp[];
  attachments: Attachment[];
  temporalFacts: TransactionTemporalFacts;
  allowedActions: TransactionWorkspaceAllowedActions;
}

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
  totalPages: number;
  hasNextPage: boolean;
  hasPreviousPage: boolean;
}

export interface ReportSectionCounts {
  responseRequired: number;
  overdueResponses: number;
  openAssignments: number;
  partialReplies: number;
  overdue: number;
  waitingReply: number;
  open: number;
}

export interface ReportTransactionRow {
  id: number;
  internalTrackingNumber: string;
  incomingNumber: string;
  incomingDate: string;
  incomingHijriDate?: string;
  subject: string;
  incomingFromDisplayName?: string;
  outgoingDepartmentsDisplayNames: string[];
  categoryName?: string;
  priority: string;
  status: string;
  responseType: string;
  responseDueDate?: string;
  assignmentDueDate?: string;
  daysRemainingForResponse?: number;
  daysSinceIncoming?: number;
  daysSinceLastFollowUp?: number | null;
  lastFollowUpDate?: string;
  responseTimingStatus?: string;
  responseTimingLabel?: string;
  daysOverdue?: number;
  createdAt: string;
  isOverdue: boolean;
}

export interface DepartmentOverdue {
  departmentId: number;
  departmentName: string;
  overdueCount: number;
}

export interface ExternalPartyReport {
  partyName: string;
  transactionCount: number;
}

export interface CategoryDistribution {
  categoryId?: number;
  categoryName: string;
  count: number;
}

export interface StatusDistribution {
  status: string;
  count: number;
}

export interface DashboardSummary {
  totalOpen: number;
  requiresResponsePending: number;
  responseOverdueCount: number;
  waitingForReply: number;
  partiallyReplied: number;
  readyForResponse: number;
  closedThisMonth: number;
  averageCompletionDays: number;
  topOverdueDepartments?: DepartmentOverdue[];
  topIncomingParties?: ExternalPartyReport[];
  byCategory?: CategoryDistribution[];
  byStatus?: StatusDistribution[];
  actionRequired?: TransactionListItem[];
}

export interface Department {
  id: number;
  name: string;
  code?: string;
  isActive: boolean;
  createdAt?: string;
}

export interface ExternalParty {
  id: number;
  name: string;
  type?: string;
  contactInfo?: string;
  isActive: boolean;
  createdAt?: string;
}

export interface User {
  id: number;
  username: string;
  fullName: string;
  email?: string;
  role: string;
  departmentId?: number;
  departmentName?: string;
  isActive: boolean;
  createdAt?: string;
}

export interface LookupItem {
  id: number;
  name: string;
  isActive: boolean;
  subLabel?: string;
}

export type ReferenceDataListParams = {
  search?: string;
  status?: string;
  sortBy?: string;
  sortDesc?: boolean;
  page?: number;
  pageSize?: number;
};

export interface LetterTemplate {
  id: number;
  code: string;
  name: string;
  content: string;
  isActive: boolean;
}

export interface FollowUpLetterPreview {
  content: string;
  targetEntity: string;
}

export interface LoginAttemptLog {
  id: number;
  username?: string;
  userId?: number;
  ipAddress?: string;
  userAgent?: string;
  succeeded: boolean;
  failureReason?: string;
  riskLevel: string;
  occurredAt: string;
}

export interface SecurityAlert {
  id: number;
  type: string;
  title: string;
  message: string;
  severity: string;
  username?: string;
  ipAddress?: string;
  userAgent?: string;
  isRead: boolean;
  createdAt: string;
  readAt?: string;
}

export interface SecurityAlertsSummary {
  unreadCount: number;
  items: SecurityAlert[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface LoginAttemptsPage {
  items: LoginAttemptLog[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface ExcelImportPreviewRowData {
  incomingNumber: string;
  incomingDate?: string | null;
  subject: string;
  assignedDepartmentName: string;
  actionTaken?: string | null;
  willCompleteResponse: boolean;
}

export interface ExcelImportPreviewRow {
  rowNumber: number;
  isValid: boolean;
  errors: string[];
  data?: ExcelImportPreviewRowData | null;
}

export interface ExcelImportPreviewResult {
  totalRows: number;
  validRows: number;
  invalidRows: number;
  rows: ExcelImportPreviewRow[];
}

export interface ExcelImportRejectedRow {
  rowNumber: number;
  errors: string[];
}

export interface ExcelImportCommitResult {
  importedCount: number;
  rejectedCount: number;
  importedTransactionIds: number[];
  rejectedRows: ExcelImportRejectedRow[];
}
