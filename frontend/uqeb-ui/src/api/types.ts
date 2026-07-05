export interface LoginResponse {
  token: string;
  username: string;
  fullName: string;
  role: string;
  departmentId?: number;
  departmentName?: string;
}

export interface SystemVersionInfo {
  backendVersion: string;
  backendCommitSha: string;
  backendBuildTimeUtc: string | null;
  environment: string;
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
  completionDate?: string | null;
  completionDays?: number | null;
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
  recurringTemplateId?: number;
  recurringPeriodLabel?: string;
  recurringRecurrenceType?: string;
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
  recurringTemplateTitle?: string;
  recurringPeriodKey?: string;
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
  letterNumber?: string;
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
  departmentResponseId?: number;
  responseDate?: string;
  departmentCompletionDays?: number;
  canAdminEdit: boolean;
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

export interface RecurringObligationsGroupCount {
  groupKey: string;
  groupLabel: string;
  count: number;
}

export interface RecurringObligationsSummary {
  total: number;
  active: number;
  upcoming: number;
  dueSoon: number;
  overdue: number;
  suspended: number;
  terminated: number;
  groups: RecurringObligationsGroupCount[];
}

export interface RecurringObligationReportRow {
  templateId: number;
  title: string;
  owningDepartmentName?: string;
  responsibleDepartmentNames: string[];
  recurrenceType: string;
  recurrenceTypeLabel: string;
  startDate: string;
  nextPeriodKey?: string;
  nextPeriodLabel?: string;
  nextDueDate?: string;
  nextDueDateHijri?: string;
  lastCompletionDate?: string;
  status: string;
  scheduleStatus: string;
  daysRemaining?: number;
  priority: string;
  generatedTransactionsCount: number;
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

export type LetterTemplateType =
  | 'FollowUp'
  | 'FirstFollowUp'
  | 'SecondFollowUp'
  | 'UrgentFollowUp'
  | 'FinalFollowUp'
  | 'LateReply'
  | 'CompletionRequest'
  | 'InternalFollowUp'
  | 'ExternalFollowUp';

export interface LetterTemplate {
  id: number;
  code: string;
  name: string;
  description?: string;
  templateType: LetterTemplateType;
  content: string;
  isActive: boolean;
  isDefault: boolean;
  sortOrder: number;
  defaultSignatoryPosition?: string;
  defaultSignatoryName?: string;
  defaultSignatoryRank?: string;
  createdAt: string;
  updatedAt?: string;
}

export interface LetterTemplateVariable {
  name: string;
  arabicDescription: string;
  example: string;
  mayBeEmpty: boolean;
}

export interface LetterTemplateValidationResult {
  unknownVariables: string[];
  isValid: boolean;
}

export interface LetterTemplatePreviewResponse {
  html: string;
}

export interface CreateLetterTemplateRequest {
  name: string;
  description?: string;
  templateType?: LetterTemplateType;
  content: string;
  isActive?: boolean;
  isDefault?: boolean;
  defaultSignatoryPosition?: string;
  defaultSignatoryName?: string;
  defaultSignatoryRank?: string;
}

export interface UpdateLetterTemplateRequest {
  name: string;
  description?: string;
  templateType?: LetterTemplateType;
  content: string;
  isActive?: boolean;
  defaultSignatoryPosition?: string;
  defaultSignatoryName?: string;
  defaultSignatoryRank?: string;
}

export interface FollowUpLetterPreview {
  content: string;
  targetEntity: string;
}

export type FollowUpPrintJobStatus =
  | 'Queued'
  | 'Claimed'
  | 'Processing'
  | 'ReadyToPrint'
  | 'PartiallyPrinted'
  | 'Completed'
  | 'Failed'
  | 'Cancelled'
  | 'Expired';

export type FollowUpPrintJobListStatusFilter =
  | 'Active'
  | 'ReadyToPrint'
  | 'Completed'
  | 'Failed'
  | 'Cancelled'
  | 'All';

export type FollowUpPrintJobPartStatus =
  | 'Pending'
  | 'Processing'
  | 'ReadyToPrint'
  | 'PartiallyReady'
  | 'Printed'
  | 'Failed'
  | 'Cancelled';

export interface FollowUpPrintFilter {
  daysSinceLastFollowUp?: number;
  excludeRecentlyPrinted?: boolean;
  printedLetterExclusionDays?: number;
  departmentId?: number;
  categoryId?: number;
  search?: string;
  page?: number;
  pageSize?: number;
}

export interface EligibleTransaction {
  transactionId: number;
  incomingNumber: string;
  subject: string;
  incomingDate: string;
  referenceDate: string;
  daysSinceReference: number;
  expectedFollowUpSequence: number;
  recentlyPrintedExcluded: boolean;
  lastPrintRequestedAt?: string;
  primaryTargetEntity?: string;
}

export interface PagedEligibleTransactions {
  totalCount: number;
  page: number;
  pageSize: number;
  items: EligibleTransaction[];
}

export interface FollowUpPrintEligibilityPreview {
  matchedCount: number;
  eligibleTransactionCount: number;
  recentlyPrintedExcludedCount: number;
  notDueYetCount: number;
  noTargetCount: number;
  estimatedLetterCount: number;
  estimatedPartCount: number;
}

export interface CreateFollowUpPrintJobRequest {
  filter: FollowUpPrintFilter;
  templateId?: number;
  responseDeadlineDays?: number;
  batchSize?: number;
  idempotencyKey?: string;
  signatoryPosition?: string;
  signatoryRank?: string;
  signatoryNameOverride?: string;
}

export interface CreateDirectPrintRequest {
  templateId?: number;
  targetDepartmentId?: number;
  targetEntityId?: number;
  targetEntityName?: string;
  followUpSequence?: number;
  responseDeadlineDays?: number;
  idempotencyKey: string;
}

export interface FollowUpPrintJobPart {
  id: number;
  jobId: number;
  partNumber: number;
  status: FollowUpPrintJobPartStatus;
  letterCount: number;
  estimatedPages: number;
  createdAt: string;
  readyAt?: string;
  printedAt?: string;
  failureReason?: string;
}

export interface FollowUpPrintJob {
  id: number;
  status: FollowUpPrintJobStatus;
  templateId: number;
  totalTransactions: number;
  totalLetters: number;
  processedLetters: number;
  readyLetters: number;
  failedLetters: number;
  skippedLetters: number;
  totalParts: number;
  readyParts: number;
  printedParts: number;
  currentPart: number;
  createdAt: string;
  startedAt?: string;
  readyAt?: string;
  completedAt?: string;
  failedAt?: string;
  cancelledAt?: string;
  failureReason?: string;
  parts: FollowUpPrintJobPart[];
}

export interface PagedFollowUpPrintJobs {
  totalCount: number;
  page: number;
  pageSize: number;
  items: FollowUpPrintJob[];
}

export interface FollowUpLetterPrintRecord {
  id: number;
  transactionId: number;
  incomingNumber: string;
  subject: string;
  targetDepartmentId?: number;
  targetEntityId?: number;
  targetEntityNameSnapshot?: string;
  templateId: number;
  followUpSequence: number;
  responseDeadlineDays?: number;
  hasDocumentSnapshot: boolean;
  printRequestedAt: string;
  printConfirmedAt?: string;
  registeredFollowUpId?: number;
  isCancelled: boolean;
  reprintOfId?: number;
}

export interface FollowUpPrintPendingSummary {
  total: number;
  withinExclusionDays: number;
  olderThanExclusionDays: number;
}

export interface FollowUpPrintRecordPrintView {
  html: string;
  usedStoredSnapshot: boolean;
  warning?: string;
}

export interface UserNotification {
  id: number;
  type: string;
  title: string;
  body: string;
  link?: string;
  isRead: boolean;
  createdAt: string;
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

export interface DepartmentResponseAttachmentDto {
  id: number;
  originalFileName: string;
  contentType?: string;
  fileSizeBytes: number;
  uploadedByName: string;
  uploadedAt: string;
}

export interface DepartmentResponseDto {
  id: number;
  transactionId: number;
  transactionSubject: string;
  internalTrackingNumber: string;
  departmentId: number;
  departmentName: string;
  responseText: string;
  status: string;
  submittedByName: string;
  submittedAt?: string;
  reviewedByName?: string;
  reviewedAt?: string;
  reviewNote?: string;
  createdAt: string;
  updatedAt?: string;
  attachments: DepartmentResponseAttachmentDto[];
}

export interface DepartmentResponseSummaryDto {
  id: number;
  transactionId: number;
  transactionSubject: string;
  internalTrackingNumber: string;
  departmentId: number;
  departmentName: string;
  status: string;
  submittedAt?: string;
  createdAt: string;
}


export interface DepartmentTransactionResponseItemDto {
  transactionId: number;
  internalTrackingNumber: string;
  subject: string;
  incomingDate?: string;
  priority: string;
  assignedDate?: string;
  departmentId: number;
  departmentName: string;
  departmentResponseId?: number;
  departmentResponseStatus?: string;
  canCreateResponse: boolean;
  canEditResponse: boolean;
  canSubmitResponse: boolean;
}

// @deprecated use DepartmentTransactionResponseItemDto
export type DepartmentTransactionItem = DepartmentTransactionResponseItemDto;

export interface DepartmentResponseStatsDto {
  totalAssigned: number;
  pendingResponse: number;
  draft: number;
  submittedForReview: number;
  returnedForCorrection: number;
  approved: number;
  rejected: number;
}

export interface RecurringTemplateDepartmentDto {
  departmentId: number;
  departmentName: string;
  sortOrder?: number;
}

export interface RecurringTemplateListItem {
  id: number;
  title: string;
  recurrenceType: string;
  status: string;
  startDate: string;
  endDate?: string;
  nextPeriodKey: string;
  nextPeriodLabel: string;
  lastGeneratedPeriodKey?: string;
  lastGeneratedPeriodLabel?: string;
  generatedTransactionsCount: number;
  nextTransactionCreationMethod: string;
}

export interface RecurringTemplateDetail extends RecurringTemplateListItem {
  subjectTemplate: string;
  incomingSourceType: string;
  incomingFromPartyId?: number;
  incomingFromPartyName?: string;
  incomingFromDepartmentId?: number;
  incomingFromDepartmentName?: string;
  categoryId: number;
  categoryName?: string;
  priority: string;
  responseType: string;
  requiresResponse: boolean;
  defaultRequiredAction: string;
  dueDaysAfterPeriodEnd: number;
  defaultReplyDueDays?: number;
  notes?: string;
  departments: RecurringTemplateDepartmentDto[];
  createdByName: string;
  createdAt: string;
  updatedAt?: string;
  pausedAt?: string;
  pausedByName?: string;
  resumedAt?: string;
  resumedByName?: string;
  terminatedAt?: string;
  terminatedByName?: string;
  terminationReason?: string;
}

export interface GenerateRecurringTransactionResponse {
  transactionId: number;
  internalTrackingNumber: string;
  periodKey: string;
  periodLabel: string;
  dueDate: string;
}

export interface RecurringTemplateTransactionItem {
  transactionId: number;
  internalTrackingNumber: string;
  subject: string;
  periodKey: string;
  periodLabel: string;
  status: string;
  incomingDate: string;
  dueDate?: string;
  closedAt?: string;
}
