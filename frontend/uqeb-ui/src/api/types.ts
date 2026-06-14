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
  responseDueDate?: string;
  daysRemainingForResponse?: number;
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
}

export interface ExternalParty {
  id: number;
  name: string;
  type?: string;
  contactInfo?: string;
  isActive: boolean;
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
}
