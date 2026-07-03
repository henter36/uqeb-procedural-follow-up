import {
  useCallback, useEffect, useRef, useState, type ReactNode,
} from 'react';
import { useParams, useNavigate, useSearchParams } from 'react-router-dom';
import { isAxiosError } from 'axios';
import { departmentResponsesApi, transactionsApi } from '../api/services';
import type {
  TransactionDetail, Assignment, FollowUp, Attachment, AuditLog, DepartmentTransactionResponseItemDto,
} from '../api/types';
import { useAuth } from '../context/useAuth';
import { useReferenceData } from '../hooks/useReferenceData';
import {
  responseTypeLabels,
  auditActionLabels, replyStatusLabels,
} from '../utils/labels';
import { getApiErrorMessage } from '../utils/apiHelpers';
import DateDisplay from '../components/DateDisplay';
import DepartmentBadges from '../components/DepartmentBadges';
import { responseTimingBadgeClass, formatCompletionDays, formatDaysSince } from '../utils/responseTiming';
import {
  PageHeader, Alert, StatusBadge, PriorityBadge, ActivityTimeline, LoadingInline, ErrorState,
} from '../components/ui';
import type { TimelineEvent } from '../components/ui';
import TransactionActionBar from '../components/transaction-workspace/TransactionActionBar';
import TransactionActionPanel from '../components/transaction-workspace/TransactionActionPanel';
import CardActionPanel from '../components/transaction-workspace/CardActionPanel';
import AssignmentFormPanel from '../components/transaction-workspace/AssignmentFormPanel';
import FollowUpFormPanel from '../components/transaction-workspace/FollowUpFormPanel';
import AttachmentFormPanel from '../components/transaction-workspace/AttachmentFormPanel';
import ReplyFormPanel from '../components/transaction-workspace/ReplyFormPanel';
import CompleteResponseFormPanel from '../components/transaction-workspace/CompleteResponseFormPanel';
import DepartmentResponseInlinePanel from '../components/transaction-workspace/DepartmentResponseInlinePanel';
import FollowUpLetterFormPanel from '../components/transaction-workspace/FollowUpLetterFormPanel';
import AdminEditAssignmentFormPanel from '../components/transaction-workspace/AdminEditAssignmentFormPanel';
import AdminEditDatesFormPanel from '../components/transaction-workspace/AdminEditDatesFormPanel';
import AdminEditResponseFormPanel from '../components/transaction-workspace/AdminEditResponseFormPanel';
import { departmentResponseStatusLabels } from '../components/transaction-workspace/departmentResponseStatusLabels';
import type { WorkspaceAction, WorkspaceActionContext } from '../components/transaction-workspace/types';
import { parseDetailTab, type DetailTab } from './transactionDetailTabs';

const GLOBAL_ACTIONS = new Set<WorkspaceAction>(['complete-response', 'follow-up-letter']);

function assignmentReplyBadgeClass(replyStatus: string, isOverdue: boolean): string {
  if (replyStatus === 'Replied') return 'badge-green';
  if (isOverdue) return 'badge-red';
  return 'badge-orange';
}

function responseStatusLabel(completed: boolean, completedDate?: string | null): ReactNode {
  if (!completed) return 'لم تتم الإفادة';
  return (
    <>
      تمت الإفادة
      {completedDate && <> بتاريخ <DateDisplay date={completedDate} /></>}
    </>
  );
}

function isPreviewableAttachment(contentType?: string): boolean {
  if (!contentType) return false;
  return contentType.startsWith('image/') || contentType === 'application/pdf';
}

function formatDaysRemaining(days?: number | null): string {
  if (days === undefined || days === null) return '—';
  if (days < 0) return `متأخر ${Math.abs(days)} يوم`;
  if (days === 0) return 'اليوم';
  return `${days} يوم`;
}

function countOpenAssignments(items: Assignment[]): number {
  return items.filter(
    (item) => item.requiresReply && item.replyStatus !== 'Replied' && item.status !== 'Cancelled',
  ).length;
}

function getCompletionDateHint(hasOfficialCompletionDate: boolean, hasEffectiveCompletionDate: boolean): string {
  if (hasOfficialCompletionDate) return 'تاريخ الإغلاق الرسمي';
  if (hasEffectiveCompletionDate) return 'محسوب من آخر تاريخ إغلاق إحالة مطلوبة';
  return 'يُحسب عند إغلاق جميع الإحالات المطلوبة';
}

function getResponseTimingLabel(isResponseOverdue: boolean, hasEffectiveCompletionDate: boolean): string {
  if (isResponseOverdue) return 'متأخرة';
  if (hasEffectiveCompletionDate) return 'مُنجزة';
  return 'في الوقت';
}

function renderDepartmentCompletionDays(completionDays?: number | null): ReactNode {
  const hasCompletionDays = completionDays != null;
  if (hasCompletionDays) {
    return <>{completionDays} <small className="text-muted">يوم</small></>;
  }

  return <span className="text-muted">لم تُنجز الإدارة</span>;
}

function hasDepartmentResponseAssignment(items: Assignment[], departmentId?: number | null): boolean {
  if (!departmentId) return false;
  return items.some(
    (item) => item.departmentId === departmentId && item.requiresReply && item.status !== 'Cancelled',
  );
}

const ACTION_TITLES: Record<WorkspaceAction, string> = {
  assignment: 'إضافة احالة',
  followup: 'إضافة تعقيب',
  attachment: 'إضافة مرفق',
  'reply-assignment': 'تسجيل رد على الاحالة',
  'reply-followup': 'تسجيل رد على التعقيب',
  'complete-response': 'تسجيل إفادة',
  'follow-up-letter': 'خطاب تعقيب PDF',
  'admin-edit-assignment': 'تعديل بيانات الاحالة',
  'admin-edit-dates': 'تصحيح التواريخ الحساسة (إداري)',
  'admin-edit-response': 'تعديل بيانات الرد (إداري)',
};

export default function TransactionDetailPage() {
  const { id } = useParams();
  if (!id) {
    return (
      <div className="loading">
        <LoadingInline />
      </div>
    );
  }
  return <TransactionDetailContent key={id} transactionId={id} />;
}

function TransactionDetailContent({ transactionId }: Readonly<{ transactionId: string }>) {
  const id = transactionId;
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const tabFromUrl = searchParams.get('tab');
  const { canEdit, canClose, isDepartmentUser, isAdmin, user } = useAuth();
  const { departments } = useReferenceData();
  const [tx, setTx] = useState<TransactionDetail | null>(null);
  const [assignments, setAssignments] = useState<Assignment[]>([]);
  const [followUps, setFollowUps] = useState<FollowUp[]>([]);
  const [attachments, setAttachments] = useState<Attachment[]>([]);
  const [auditLogs, setAuditLogs] = useState<AuditLog[]>([]);
  const [auditPage, setAuditPage] = useState(1);
  const [auditHasMore, setAuditHasMore] = useState(false);
  const [auditLoadingMore, setAuditLoadingMore] = useState(false);
  const [activeTab, setActiveTab] = useState<DetailTab>(() => parseDetailTab(tabFromUrl));
  const [auditTabLoading, setAuditTabLoading] = useState(
    () => {
      const tab = parseDetailTab(tabFromUrl);
      return tab === 'timeline' || tab === 'audit';
    },
  );
  const [auditTabError, setAuditTabError] = useState('');
  const auditDataLoadedRef = useRef(false);
  const [assignmentsLoading, setAssignmentsLoading] = useState(true);
  const [followUpsLoading, setFollowUpsLoading] = useState(true);
  const [attachmentsLoading, setAttachmentsLoading] = useState(true);
  const [assignmentsError, setAssignmentsError] = useState('');
  const [followUpsError, setFollowUpsError] = useState('');
  const [attachmentsError, setAttachmentsError] = useState('');
  const [activeAction, setActiveAction] = useState<WorkspaceAction | null>(null);
  const [actionContext, setActionContext] = useState<WorkspaceActionContext>({});
  const [actionDirty, setActionDirty] = useState(false);
  const [message, setMessage] = useState('');
  const [error, setError] = useState('');
  const [workspaceLoading, setWorkspaceLoading] = useState(true);
  const [departmentResponseItem, setDepartmentResponseItem] = useState<DepartmentTransactionResponseItemDto | null>(null);
  const [departmentResponseItemError, setDepartmentResponseItemError] = useState('');
  const responsePanelRef = useRef<HTMLElement | null>(null);
  const departmentResponseRequestRef = useRef(0);

  const applyWorkspaceData = useCallback((data: import('../api/types').TransactionWorkspace) => {
    setTx(data.transaction);
    setAssignments(data.assignments ?? []);
    setFollowUps(data.followUps ?? []);
    setAttachments(data.attachments ?? []);
  }, []);

  const handleWorkspaceFailure = useCallback((err: unknown, signal?: AbortSignal) => {
    if (signal?.aborted || (isAxiosError(err) && err.code === 'ERR_CANCELED')) return;
    if (isAxiosError(err) && err.response?.status === 404) {
      navigate('/transactions');
      return;
    }
    setError('تعذر تحميل بيانات المعاملة');
    setTx(null);
  }, [navigate]);

  const loadWorkspace = useCallback(async (options?: { signal?: AbortSignal; silent?: boolean }) => {
    if (!id) return;
    if (!options?.silent) {
      setAssignmentsLoading(true);
      setFollowUpsLoading(true);
      setAttachmentsLoading(true);
    }
    setAssignmentsError('');
    setFollowUpsError('');
    setAttachmentsError('');
    if (!options?.silent) setError('');
    try {
      const res = await transactionsApi.getWorkspace(+id, { signal: options?.signal });
      if (options?.signal?.aborted) return;
      applyWorkspaceData(res.data);
    } catch (err: unknown) {
      handleWorkspaceFailure(err, options?.signal);
    } finally {
      if (!options?.signal?.aborted) {
        setWorkspaceLoading(false);
        setAssignmentsLoading(false);
        setFollowUpsLoading(false);
        setAttachmentsLoading(false);
      }
    }
  }, [id, applyWorkspaceData, handleWorkspaceFailure]);

  const loadDepartmentResponseItem = useCallback(async () => {
    const requestId = departmentResponseRequestRef.current + 1;
    departmentResponseRequestRef.current = requestId;

    if (!isDepartmentUser) {
      setDepartmentResponseItem(null);
      setDepartmentResponseItemError('');
      return;
    }

    setDepartmentResponseItemError('');
    try {
      const res = await departmentResponsesApi.getDepartmentTransactions();
      if (departmentResponseRequestRef.current !== requestId) return;
      setDepartmentResponseItem(res.data.find((item) => item.transactionId === +id) ?? null);
    } catch {
      if (departmentResponseRequestRef.current !== requestId) return;
      setDepartmentResponseItem(null);
      setDepartmentResponseItemError('تعذر تحميل حالة إفادة الإدارة. أعد المحاولة.');
    }
  }, [id, isDepartmentUser]);

  const loadAssignments = useCallback(async () => {
    if (!id) return;
    setAssignmentsLoading(true);
    setAssignmentsError('');
    try {
      const res = await transactionsApi.getAssignments(+id);
      setAssignments(res.data ?? []);
    } catch {
      setAssignmentsError('تعذر تحميل الاحالةات');
    } finally {
      setAssignmentsLoading(false);
    }
  }, [id]);

  const loadFollowUps = useCallback(async () => {
    if (!id) return;
    setFollowUpsLoading(true);
    setFollowUpsError('');
    try {
      const res = await transactionsApi.getFollowUps(+id);
      setFollowUps(res.data ?? []);
    } catch {
      setFollowUpsError('تعذر تحميل التعقيبات');
    } finally {
      setFollowUpsLoading(false);
    }
  }, [id]);

  const loadAttachments = useCallback(async () => {
    if (!id) return;
    setAttachmentsLoading(true);
    setAttachmentsError('');
    try {
      const res = await transactionsApi.getAttachments(+id);
      setAttachments(res.data ?? []);
    } catch {
      setAttachmentsError('تعذر تحميل المرفقات');
      setAttachments([]);
    } finally {
      setAttachmentsLoading(false);
    }
  }, [id]);

  const loadAuditLog = useCallback(async (page: number, append: boolean) => {
    if (!id) return;
    if (append) setAuditLoadingMore(true);
    else setAuditTabError('');
    try {
      const res = await transactionsApi.getAuditLog(+id, page);
      setAuditLogs((prev) => (append ? [...prev, ...res.data.items] : res.data.items));
      setAuditPage(page);
      setAuditHasMore(res.data.hasNextPage);
      auditDataLoadedRef.current = true;
    } catch {
      if (!append) {
        setAuditTabError('تعذر تحميل سجل التدقيق');
        setAuditLogs([]);
        setAuditHasMore(false);
      }
    } finally {
      if (append) setAuditLoadingMore(false);
    }
  }, [id]);

  const loadAuditTabData = useCallback(async () => {
    if (!id || auditDataLoadedRef.current) {
      setAuditTabLoading(false);
      return;
    }
    setAuditTabLoading(true);
    setAuditTabError('');
    try {
      const res = await transactionsApi.getAuditLog(+id, 1);
      setAuditLogs(res.data.items);
      setAuditPage(1);
      setAuditHasMore(res.data.hasNextPage);
      auditDataLoadedRef.current = true;
    } catch {
      setAuditTabError('تعذر تحميل سجل التدقيق');
      setAuditLogs([]);
      setAuditHasMore(false);
    } finally {
      setAuditTabLoading(false);
    }
  }, [id]);

  const handleSelectTab = async (tab: DetailTab) => {
    setActiveTab(tab);
    if (tab === 'timeline' || tab === 'audit') {
      try {
        await loadAuditTabData();
      } catch {
        setAuditTabError('تعذر تحميل سجل التدقيق');
      }
    }
  };

  useEffect(() => {
    const controller = new AbortController();
    let active = true;

    transactionsApi.getWorkspace(+id, { signal: controller.signal })
      .then((res) => {
        if (!active || controller.signal.aborted) return;
        applyWorkspaceData(res.data);
      })
      .catch((err: unknown) => {
        if (!active || controller.signal.aborted) return;
        handleWorkspaceFailure(err, controller.signal);
      })
      .finally(() => {
        if (!active || controller.signal.aborted) return;
        setWorkspaceLoading(false);
        setAssignmentsLoading(false);
        setFollowUpsLoading(false);
        setAttachmentsLoading(false);
      });

    return () => {
      active = false;
      controller.abort();
    };
  }, [id, applyWorkspaceData, handleWorkspaceFailure]);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    loadDepartmentResponseItem().catch(() => undefined);
  }, [loadDepartmentResponseItem]);

  useEffect(() => {
    if (activeAction !== 'complete-response') return;

    const panel = responsePanelRef.current;
    panel?.scrollIntoView({ behavior: 'smooth', block: 'start' });
    const focusTimeout = globalThis.setTimeout(() => {
      const field = panel?.querySelector<HTMLElement>(
        '#department-response-text, #response-summary, textarea, input',
      );
      field?.focus();
    }, 0);
    return () => globalThis.clearTimeout(focusTimeout);
  }, [activeAction]);

  useEffect(() => {
    const tab = parseDetailTab(tabFromUrl);
    if (tab !== 'timeline' && tab !== 'audit') return;

    let active = true;

    const loadAuditFromUrl = async () => {
      try {
        const res = await transactionsApi.getAuditLog(+id, 1);
        if (!active) return;
        setAuditLogs(res.data.items);
        setAuditPage(1);
        setAuditHasMore(res.data.hasNextPage);
        auditDataLoadedRef.current = true;
      } catch {
        if (!active) return;
        setAuditTabError('تعذر تحميل سجل التدقيق');
        setAuditLogs([]);
        setAuditHasMore(false);
      } finally {
        if (active) setAuditTabLoading(false);
      }
    };

    loadAuditFromUrl().catch(() => {
      if (active) setAuditTabError('تعذر تحميل سجل التدقيق');
    });

    return () => { active = false; };
  }, [id, tabFromUrl]);

  const refreshAuditIfLoaded = useCallback(async () => {
    if (!auditDataLoadedRef.current) return;
    auditDataLoadedRef.current = false;
    await loadAuditTabData();
  }, [loadAuditTabData]);

  const resetAndCloseAction = useCallback(() => {
    setActiveAction(null);
    setActionContext({});
    setActionDirty(false);
  }, []);

  const closeAction = useCallback(() => {
    if (actionDirty && !globalThis.confirm('يوجد بيانات غير محفوظة. هل تريد إغلاق النموذج؟')) {
      return;
    }
    resetAndCloseAction();
  }, [actionDirty, resetAndCloseAction]);

  const openAction = useCallback((action: WorkspaceAction, ctx: WorkspaceActionContext = {}) => {
    if (activeAction && activeAction !== action && actionDirty) {
      if (!globalThis.confirm('يوجد بيانات غير محفوظة. هل تريد التبديل بين النماذج؟')) {
        return;
      }
    }
    setActiveAction(action);
    setActionContext(ctx);
    setActionDirty(false);
  }, [activeAction, actionDirty]);

  const toggleAction = useCallback((action: WorkspaceAction, ctx: WorkspaceActionContext = {}) => {
    if (activeAction === action && !ctx.replyAssignmentId && !ctx.replyFollowUpId) {
      closeAction();
      return;
    }
    openAction(action, ctx);
  }, [activeAction, closeAction, openAction]);

  const handleActionSuccess = useCallback(async (
    successMessage: string,
    refreshers: Array<() => void | Promise<void>>,
  ) => {
    setMessage(successMessage);
    setError('');
    resetAndCloseAction();
    const results = await Promise.allSettled([
      ...refreshers.map((refresh) => Promise.resolve(refresh())),
      refreshAuditIfLoaded(),
    ]);
    const allFailed = results.length > 0 && results.every((result) => result.status === 'rejected');
    if (allFailed) {
      setError('تم الحفظ لكن تعذر تحديث بعض الأقسام. حاول تحديث الصفحة.');
    }
  }, [resetAndCloseAction, refreshAuditIfLoaded]);

  const handleAssignmentSuccess = async () => {
    await handleActionSuccess('تم إضافة الاحالة بنجاح.', [() => loadWorkspace({ silent: true })]);
  };

  const handleFollowUpSuccess = async () => {
    await handleActionSuccess('تم إضافة التعقيب بنجاح.', [() => loadWorkspace({ silent: true })]);
  };

  const handleAttachmentSuccess = async () => {
    await handleActionSuccess('تم رفع المرفق بنجاح.', [() => loadWorkspace({ silent: true })]);
  };

  const handleReplyAssignmentSuccess = async () => {
    await handleActionSuccess('تم تسجيل الرد بنجاح.', [() => loadWorkspace({ silent: true })]);
  };

  const handleAdminEditAssignmentSuccess = async (updated: import('../api/types').Assignment) => {
    setAssignments((prev) => prev.map((a) => (a.id === updated.id
      ? {
          ...a,
          ...updated,
          departmentResponseId: a.departmentResponseId,
          responseDate: a.responseDate,
          departmentCompletionDays: a.departmentCompletionDays,
          canAdminEdit: a.canAdminEdit,
        }
      : a)));
    closeAction();
  };

  const handleAdminEditDatesSuccess = (updated: import('../api/types').TransactionDetail) => {
    setTx(updated);
    closeAction();
  };

  const handleAdminEditResponseSuccess = () => {
    closeAction();
    loadWorkspace({ silent: true }).catch(() => undefined);
  };

  const handleReplyFollowUpSuccess = async () => {
    await handleActionSuccess('تم تسجيل الرد بنجاح.', [() => loadWorkspace({ silent: true })]);
  };

  const handleCompleteResponseSuccess = async (result?: { attachmentWarning?: string }) => {
    const successMessage = result?.attachmentWarning ?? 'تم تسجيل الإفادة بنجاح.';
    await handleActionSuccess(successMessage, [() => loadWorkspace({ silent: true })]);
  };

  const handleDepartmentResponseChanged = async (response: import('../api/types').DepartmentResponseDto) => {
    setDepartmentResponseItem((current) => {
      const editable = response.status === 'Draft' || response.status === 'ReturnedForCorrection';
      return {
        transactionId: response.transactionId,
        internalTrackingNumber: response.internalTrackingNumber,
        subject: response.transactionSubject,
        priority: current?.priority ?? tx?.priority ?? 'Normal',
        departmentId: response.departmentId,
        departmentName: response.departmentName,
        departmentResponseId: response.id,
        departmentResponseStatus: response.status,
        canCreateResponse: false,
        canEditResponse: editable,
        canSubmitResponse: editable,
      };
    });

    await Promise.all([
      loadWorkspace({ silent: true }),
      refreshAuditIfLoaded(),
    ]);
  };

  const handleCloseTransaction = async () => {
    if (!tx) return;
    const needsResponse = tx.requiresResponse || tx.responseType !== 'None';
    if (needsResponse && !tx.responseCompleted) {
      setError('لا يمكن إغلاق المعاملة قبل تسجيل الإفادة.');
      return;
    }
    if (!globalThis.confirm('هل تريد إغلاق المعاملة؟')) return;
    setError('');
    try {
      await transactionsApi.close(+id);
      await loadWorkspace({ silent: true });
      setMessage('تم إغلاق المعاملة');
    } catch (err: unknown) {
      setError(getApiErrorMessage(err));
    }
  };

  const downloadAttachment = async (attachmentId: number, fileName: string) => {
    const res = await transactionsApi.downloadAttachment(+id, attachmentId);
    const url = globalThis.URL.createObjectURL(res.data);
    const a = document.createElement('a');
    a.href = url;
    a.download = fileName;
    a.click();
    globalThis.URL.revokeObjectURL(url);
  };

  const previewAttachment = async (attachmentId: number, contentType?: string) => {
    if (!isPreviewableAttachment(contentType)) return;
    const res = await transactionsApi.downloadAttachment(+id, attachmentId);
    const url = globalThis.URL.createObjectURL(res.data);
    globalThis.open(url, '_blank', 'noopener,noreferrer');
    globalThis.setTimeout(() => globalThis.URL.revokeObjectURL(url), 60_000);
  };

  if (workspaceLoading) return <div className="loading"><LoadingInline /></div>;

  if (error && !tx) {
    return (
      <ErrorState
        title="تعذر تحميل المعاملة"
        description={error}
        action={(
          <button
            type="button"
            className="btn btn-primary"
            onClick={() => {
              setWorkspaceLoading(true);
              loadWorkspace().catch(() => {});
            }}
          >
            إعادة المحاولة
          </button>
        )}
      />
    );
  }

  if (!tx) return <div className="loading"><LoadingInline /></div>;

  const needsResponse = tx.requiresResponse || tx.responseType !== 'None';
  const isTerminal = tx.status === 'Closed' || tx.status === 'Cancelled' || tx.status === 'Archived';
  const hasPendingDepts = tx.pendingDepartmentNames.length > 0;
  const departmentResponseStatus = departmentResponseItem?.departmentResponseStatus;
  const canDepartmentUserRegisterResponse = isDepartmentUser
    && hasDepartmentResponseAssignment(assignments, user?.departmentId)
    && Boolean(departmentResponseItem?.canCreateResponse || departmentResponseItem?.canEditResponse);
  const canRegisterResponse = (
    canClose || canDepartmentUserRegisterResponse
  ) && needsResponse && !tx.responseCompleted && !isTerminal;
  const responseActionLabel = isDepartmentUser && departmentResponseStatus
    ? 'استكمال إفادة'
    : 'تسجيل إفادة';
  const departmentResponseActionStatusLabel = isDepartmentUser && departmentResponseStatus && !canDepartmentUserRegisterResponse
    ? departmentResponseStatusLabels[departmentResponseStatus] ?? departmentResponseStatus
    : undefined;
  const canShowClose = canClose && !isTerminal && (!needsResponse || tx.responseCompleted);
  const showMutationActions = canEdit && !isDepartmentUser;
  const canReply = canEdit && !isDepartmentUser;
  const openAssignmentsCount = countOpenAssignments(assignments);

  // Derive effective close date from assignments when ClosedAt is not set
  const requiredAssignments = assignments.filter((a) => a.requiresReply && a.status !== 'Cancelled');
  const allRequiredHaveResponse = requiredAssignments.length > 0 && requiredAssignments.every((a) => a.responseDate != null);
  const derivedCompletionDate: string | null = allRequiredHaveResponse
    ? requiredAssignments.reduce<string | null>((max, a) => (!max || a.responseDate! > max ? a.responseDate! : max), null)
    : null;
  const effectiveCompletionDate = tx.completionDate ?? derivedCompletionDate;
  const toUtcDateOnlyTime = (value: string) => {
    const date = new Date(value);
    return Date.UTC(date.getUTCFullYear(), date.getUTCMonth(), date.getUTCDate());
  };

  const effectiveCompletionDays = tx.completionDays ?? (
    effectiveCompletionDate && tx.incomingDate
      ? Math.max(0, Math.floor((toUtcDateOnlyTime(effectiveCompletionDate) - toUtcDateOnlyTime(tx.incomingDate)) / 86400000))
      : null
  );
  const hasOfficialCompletionDate = Boolean(tx.completionDate);
  const hasEffectiveCompletionDate = Boolean(effectiveCompletionDate);
  const hasEffectiveCompletionDays = effectiveCompletionDays != null;
  const completionDateHint = getCompletionDateHint(hasOfficialCompletionDate, hasEffectiveCompletionDate);
  const completionDaysLabel = hasEffectiveCompletionDays ? 'أيام إنجاز المعاملة' : 'الأيام المفتوحة';
  const completionDaysValue = hasEffectiveCompletionDays
    ? formatCompletionDays(effectiveCompletionDays)
    : formatDaysSince(tx.daysSinceIncoming, '0');
  const completionDaysHint = hasEffectiveCompletionDays
    ? 'محسوب تلقائيًا: تاريخ الإغلاق − تاريخ الوارد'
    : 'محسوب تلقائيًا: اليوم − تاريخ الوارد';
  const responseTimingLabel = getResponseTimingLabel(tx.isResponseOverdue, hasEffectiveCompletionDate);

  const replyAssignmentId = actionContext.replyAssignmentId;
  const replyFollowUpId = actionContext.replyFollowUpId;
  const adminEditAssignmentId = actionContext.adminEditAssignmentId;
  const adminEditResponseId = actionContext.adminEditResponseId;
  const existingDepartmentIds = assignments.map((a) => a.departmentId);
  const isGlobalActionOpen = activeAction !== null && GLOBAL_ACTIONS.has(activeAction);

  const timelineEvents: TimelineEvent[] = auditLogs.map((log) => ({
    id: log.id,
    action: auditActionLabels[log.action] || log.action,
    userName: log.userName,
    date: log.createdAt,
    detail: log.newValue || log.oldValue || undefined,
  }));

  const auditErrorTitle = activeTab === 'timeline'
    ? 'تعذر تحميل الخط الزمني'
    : 'تعذر تحميل سجل التدقيق';

  const detailsTabContent = (
    <div className="transaction-details-stack">
      <section className="card transaction-hero-card" aria-label="معلومات المعاملة">
        <div className="transaction-hero-top">
          <div className="transaction-hero-title-block">
            <div className="transaction-hero-title-row">
              <h2 className="transaction-hero-number">{tx.incomingNumber}</h2>
              <StatusBadge status={tx.status} isOverdue={tx.isOverdue} />
              <PriorityBadge priority={tx.priority} />
              {tx.isOverdue && <span className="badge badge-red">متأخرة</span>}
              {tx.hasPendingAssignments && <span className="badge badge-orange">باقي إجراء</span>}
              {tx.responseTimingLabel && tx.requiresResponse && (
                <span className={`badge badge-spaced ${responseTimingBadgeClass(tx.responseTimingStatus)}`}>
                  {tx.responseTimingLabel}
                </span>
              )}
            </div>
            <p className="transaction-hero-subject">{tx.subject}</p>
            <div className="transaction-hero-meta">
              <span>{tx.incomingFrom || '—'}</span>
              <span className="transaction-hero-meta-sep">•</span>
              <span>{tx.categoryName || '—'}</span>
              <span className="transaction-hero-meta-sep">•</span>
              <DepartmentBadges names={tx.outgoingDepartmentNames} />
            </div>
          </div>
          <div className="transaction-hero-actions">
            <TransactionActionBar
              transactionId={id}
              canEdit={canEdit}
              canClose={canClose}
              isDepartmentUser={isDepartmentUser}
              canRegisterResponse={canRegisterResponse}
              responseActionLabel={responseActionLabel}
              responseStatusLabel={departmentResponseActionStatusLabel}
              canShowClose={canShowClose}
              hasPendingDepts={hasPendingDepts}
              activeAction={activeAction}
              onAction={toggleAction}
              onCloseTransaction={handleCloseTransaction}
            />
            {isDepartmentUser && departmentResponseItemError && (
              <div className="dept-response-item-error">
                <span className="text-danger">{departmentResponseItemError}</span>
                <button
                  type="button"
                  className="btn btn-sm btn-outline"
                  onClick={() => loadDepartmentResponseItem().catch(() => {})}
                >
                  إعادة المحاولة
                </button>
              </div>
            )}
          </div>
        </div>

        <div className="transaction-metric-grid">
          <div className="transaction-metric-tile">
            <span className="transaction-metric-label">تاريخ الوارد</span>
            <span className="transaction-metric-value"><DateDisplay date={tx.incomingDate} /></span>
            <small className="text-muted metric-hint">بداية عمر المعاملة وأيام الإنجاز</small>
          </div>
          <div className="transaction-metric-tile">
            <span className="transaction-metric-label">تاريخ استحقاق المعاملة</span>
            <span className="transaction-metric-value">
              {tx.responseDueDate ? <DateDisplay date={tx.responseDueDate} /> : '—'}
            </span>
            <small className="text-muted metric-hint">آخر تاريخ متوقع لإغلاق جميع الإحالات</small>
          </div>
          <div className="transaction-metric-tile">
            <span className="transaction-metric-label">تاريخ إغلاق المعاملة</span>
            <span className="transaction-metric-value">
              {effectiveCompletionDate ? <DateDisplay date={effectiveCompletionDate} /> : '—'}
            </span>
            <small className="text-muted metric-hint">
              {completionDateHint}
            </small>
          </div>
          <div className="transaction-metric-tile">
            <span className="transaction-metric-label">
              {completionDaysLabel}
            </span>
            <span className="transaction-metric-value">
              {completionDaysValue}
            </span>
            <small className="text-muted metric-hint">
              {completionDaysHint}
            </small>
          </div>
          <div className={`transaction-metric-tile${tx.isResponseOverdue ? ' metric-tile-overdue' : ''}`}>
            <span className="transaction-metric-label">حالة التأخر</span>
            <span className={`transaction-metric-value${tx.isResponseOverdue ? ' text-danger' : ' text-success'}`}>
              {responseTimingLabel}
            </span>
            <small className="text-muted metric-hint">
              {tx.responseDueDate
                ? `${formatDaysRemaining(tx.daysRemainingForResponse)} حتى الاستحقاق`
                : 'لم يُحدَّد تاريخ استحقاق'}
            </small>
          </div>
          <div className="transaction-metric-tile">
            <span className="transaction-metric-label">منذ آخر تعقيب</span>
            <span className="transaction-metric-value">{formatDaysSince(tx.daysSinceLastFollowUp)}</span>
          </div>
          <div className="transaction-metric-tile">
            <span className="transaction-metric-label">احالةات مفتوحة</span>
            <span className="transaction-metric-value">{openAssignmentsCount}</span>
          </div>
          <div className="transaction-metric-tile">
            <span className="transaction-metric-label">المرفقات</span>
            <span className="transaction-metric-value">{attachments.length}</span>
          </div>
        </div>

        {isAdmin && (
          <div className="admin-dates-edit-bar">
            <button
              type="button"
              className={`btn btn-sm btn-outline${activeAction === 'admin-edit-dates' ? ' active' : ''}`}
              aria-pressed={activeAction === 'admin-edit-dates'}
              onClick={() => toggleAction('admin-edit-dates')}
            >
              تصحيح التواريخ (إداري)
            </button>
          </div>
        )}

        {activeAction === 'admin-edit-dates' && (
          <CardActionPanel
            title={ACTION_TITLES['admin-edit-dates']}
            onClose={closeAction}
            testId="admin-edit-dates-form-panel"
          >
            <AdminEditDatesFormPanel
              transactionId={+id}
              transaction={tx}
              onDirtyChange={setActionDirty}
              onCancel={closeAction}
              onSuccess={handleAdminEditDatesSuccess}
            />
          </CardActionPanel>
        )}

        <details className="transaction-hero-details">
          <summary>تفاصيل إضافية</summary>
          <div className="detail-grid mt-3">
            <div><strong>رقم التتبع:</strong> {tx.internalTrackingNumber}</div>
            <div><strong>تاريخ الوارد:</strong> <DateDisplay date={tx.incomingDate} /></div>
            <div><strong>نوع الجهة:</strong> {tx.incomingSourceType === 'Internal' ? 'داخلية' : 'خارجية'}</div>
            {tx.outgoingNumber && <div><strong>رقم الصادر:</strong> {tx.outgoingNumber}</div>}
            {tx.outgoingDate && <div><strong>تاريخ الصادر:</strong> <DateDisplay date={tx.outgoingDate} /></div>}
            {needsResponse && (
              <>
                <div><strong>مطلوب إفادة:</strong> نعم ({responseTypeLabels[tx.responseType] || tx.responseType})</div>
                <div><strong>حالة الإفادة:</strong> {responseStatusLabel(tx.responseCompleted, tx.responseCompletedDate)}</div>
                {tx.responseSummary && <div className="full-width"><strong>ملخص الإفادة:</strong> {tx.responseSummary}</div>}
              </>
            )}
            {tx.notes && <div className="full-width"><strong>ملاحظات:</strong> {tx.notes}</div>}
          </div>
        </details>
      </section>

      {isGlobalActionOpen && activeAction && (
        <TransactionActionPanel
          title={activeAction === 'complete-response' ? responseActionLabel : ACTION_TITLES[activeAction]}
          open
          onClose={closeAction}
          panelRef={activeAction === 'complete-response' ? responsePanelRef : undefined}
          prominent={activeAction === 'complete-response'}
        >
          {activeAction === 'complete-response' && isDepartmentUser && (
            <DepartmentResponseInlinePanel
              transactionId={+id}
              initialItem={departmentResponseItem}
              onDirtyChange={setActionDirty}
              onCancel={closeAction}
              onMessage={(nextMessage) => {
                setMessage(nextMessage);
                setError('');
              }}
              onChanged={handleDepartmentResponseChanged}
            />
          )}
          {activeAction === 'complete-response' && !isDepartmentUser && (
            <CompleteResponseFormPanel
              transactionId={+id}
              responseType={tx.responseType}
              onDirtyChange={setActionDirty}
              onCancel={closeAction}
              onSuccess={handleCompleteResponseSuccess}
            />
          )}
          {activeAction === 'follow-up-letter' && (
            <FollowUpLetterFormPanel
              transactionId={+id}
              tx={tx}
              assignments={assignments}
              onDirtyChange={setActionDirty}
              onCancel={closeAction}
              onDownloaded={() => setMessage('تم تحميل خطاب التعقيب بنجاح.')}
            />
          )}
        </TransactionActionPanel>
      )}

      <div className="transaction-sections-grid">
        <section className="card transaction-section-card" aria-label="الاحالات والردود">
          <div className="section-card-header">
            <div className="section-card-title">
              <span className="section-card-icon" aria-hidden>↪</span>
              <h3>الاحالات والردود</h3>
              <span className="section-card-count">{assignments.length} احالة</span>
            </div>
            {showMutationActions && (
              <button
                type="button"
                className={`btn btn-secondary btn-sm${activeAction === 'assignment' ? ' active' : ''}`}
                aria-pressed={activeAction === 'assignment'}
                onClick={() => toggleAction('assignment')}
              >
                + إضافة احالة
              </button>
            )}
          </div>

          {activeAction === 'assignment' && (
            <CardActionPanel
              title={ACTION_TITLES.assignment}
              onClose={closeAction}
              testId="assignment-form-panel"
            >
              <AssignmentFormPanel
                transactionId={+id}
                departments={departments}
                existingDepartmentIds={existingDepartmentIds}
                onDirtyChange={setActionDirty}
                onCancel={closeAction}
                onSuccess={handleAssignmentSuccess}
              />
            </CardActionPanel>
          )}

          {activeAction === 'reply-assignment' && replyAssignmentId && (
            <CardActionPanel
              title={ACTION_TITLES['reply-assignment']}
              onClose={closeAction}
              testId="reply-assignment-form-panel"
            >
              <ReplyFormPanel
                title="تسجيل رد على الاحالة"
                onDirtyChange={setActionDirty}
                onCancel={closeAction}
                onSubmit={(payload) => transactionsApi.replyAssignment(+id, replyAssignmentId, payload)}
                onSuccess={handleReplyAssignmentSuccess}
              />
            </CardActionPanel>
          )}

          {activeAction === 'admin-edit-assignment' && adminEditAssignmentId && (
            <CardActionPanel
              title={ACTION_TITLES['admin-edit-assignment']}
              onClose={closeAction}
              testId="admin-edit-assignment-form-panel"
            >
              <AdminEditAssignmentFormPanel
                key={adminEditAssignmentId}
                transactionId={+id}
                assignmentId={adminEditAssignmentId}
                initialAssignment={assignments.find((a) => a.id === adminEditAssignmentId)}
                onDirtyChange={setActionDirty}
                onCancel={closeAction}
                onSuccess={handleAdminEditAssignmentSuccess}
              />
            </CardActionPanel>
          )}

          {activeAction === 'admin-edit-response' && adminEditResponseId && (
            <CardActionPanel
              title={ACTION_TITLES['admin-edit-response']}
              onClose={closeAction}
              testId="admin-edit-response-form-panel"
            >
              <AdminEditResponseFormPanel
                responseId={adminEditResponseId}
                onDirtyChange={setActionDirty}
                onCancel={closeAction}
                onSuccess={handleAdminEditResponseSuccess}
              />
            </CardActionPanel>
          )}

          {assignmentsLoading && <LoadingInline label="جاري تحميل الاحالةات..." />}
          {assignmentsError && (
            <Alert variant="error">
              {assignmentsError}
              <button type="button" className="btn btn-sm btn-outline ms-2" onClick={loadAssignments}>
                إعادة المحاولة
              </button>
            </Alert>
          )}
          {!assignmentsLoading && !assignmentsError && assignments.length === 0 && (
            <div className="section-empty-state">
              <p>لا توجد احالةات مسجلة لهذه المعاملة.</p>
              {showMutationActions && (
                <button type="button" className="btn btn-primary btn-sm" onClick={() => toggleAction('assignment')}>
                  إضافة أول احالة
                </button>
              )}
            </div>
          )}
          {!assignmentsLoading && !assignmentsError && assignments.length > 0 && (
            <div className="table-wrapper section-data-list">
              <table className="data-table data-table-compact">
                <thead>
                  <tr>
                    <th>الإدارة</th>
                    <th>رقم الخطاب</th>
                    <th>تاريخ الإحالة</th>
                    <th>تاريخ استحقاق الإدارة</th>
                    <th>تاريخ إنجاز الإدارة</th>
                    <th>أيام إنجاز الإدارة</th>
                    <th>الحالة</th>
                    <th></th>
                  </tr>
                </thead>
                <tbody>
                  {assignments.map((a) => (
                    <tr key={a.id} className={a.isOverdue ? 'row-overdue' : ''}>
                      <td>
                        <div>{a.departmentName}</div>
                        {a.requiredAction && <div className="text-muted">{a.requiredAction}</div>}
                      </td>
                      <td>{a.letterNumber || '—'}</td>
                      <td><DateDisplay date={a.assignedDate} /></td>
                      <td>{a.dueDate ? <DateDisplay date={a.dueDate} /> : '—'}</td>
                      <td>{a.responseDate ? <DateDisplay date={a.responseDate} /> : '—'}</td>
                      <td>
                        {renderDepartmentCompletionDays(a.departmentCompletionDays)}
                      </td>
                      <td>
                        <span className={`badge ${assignmentReplyBadgeClass(a.replyStatus, a.isOverdue)}`}>
                          {replyStatusLabels[a.replyStatus] || a.replyStatus}
                        </span>
                        {a.isOverdue && a.replyStatus !== 'Replied' && (
                          <span className="badge badge-red ms-1">متأخرة</span>
                        )}
                      </td>
                      <td className="assignment-actions-cell">
                        {a.requiresReply && a.replyStatus !== 'Replied' && a.status !== 'Cancelled' && canReply && (
                          <button
                            type="button"
                            className="btn btn-sm btn-outline"
                            onClick={() => openAction('reply-assignment', { replyAssignmentId: a.id })}
                          >
                            تسجيل رد
                          </button>
                        )}
                        {isAdmin && (
                          <button
                            type="button"
                            className="btn btn-sm btn-outline"
                            onClick={() => openAction('admin-edit-assignment', { adminEditAssignmentId: a.id })}
                          >
                            تعديل
                          </button>
                        )}
                        {isAdmin && a.departmentResponseId && (
                          <button
                            type="button"
                            className="btn btn-sm btn-outline"
                            onClick={() => openAction('admin-edit-response', { adminEditResponseId: a.departmentResponseId })}
                          >
                            تعديل الرد
                          </button>
                        )}
                        {a.replySummary && <div className="text-muted reply-summary">{a.replySummary}</div>}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </section>

        <section className="card transaction-section-card" aria-label="التعقيبات والردود">
          <div className="section-card-header">
            <div className="section-card-title">
              <span className="section-card-icon" aria-hidden>✉</span>
              <h3>التعقيبات والردود</h3>
              <span className="section-card-count">{followUps.length} تعقيب</span>
            </div>
            {showMutationActions && (
              <button
                type="button"
                className={`btn btn-secondary btn-sm${activeAction === 'followup' ? ' active' : ''}`}
                aria-pressed={activeAction === 'followup'}
                onClick={() => toggleAction('followup')}
              >
                + إضافة تعقيب
              </button>
            )}
          </div>

          {activeAction === 'followup' && (
            <CardActionPanel
              title={ACTION_TITLES.followup}
              onClose={closeAction}
              testId="followup-form-panel"
            >
              <FollowUpFormPanel
                transactionId={+id}
                daysSinceLastFollowUp={tx.daysSinceLastFollowUp}
                onDirtyChange={setActionDirty}
                onCancel={closeAction}
                onSuccess={handleFollowUpSuccess}
              />
            </CardActionPanel>
          )}

          {activeAction === 'reply-followup' && replyFollowUpId && (
            <CardActionPanel
              title={ACTION_TITLES['reply-followup']}
              onClose={closeAction}
              testId="reply-followup-form-panel"
            >
              <ReplyFormPanel
                title="تسجيل رد على التعقيب"
                onDirtyChange={setActionDirty}
                onCancel={closeAction}
                onSubmit={(payload) => transactionsApi.replyFollowUp(+id, replyFollowUpId, payload)}
                onSuccess={handleReplyFollowUpSuccess}
              />
            </CardActionPanel>
          )}

          {followUpsLoading && <LoadingInline label="جاري تحميل التعقيبات..." />}
          {followUpsError && (
            <Alert variant="error">
              {followUpsError}
              <button type="button" className="btn btn-sm btn-outline ms-2" onClick={loadFollowUps}>
                إعادة المحاولة
              </button>
            </Alert>
          )}
          {!followUpsLoading && !followUpsError && followUps.length === 0 && (
            <div className="section-empty-state">
              <p>لا توجد تعقيبات مسجلة لهذه المعاملة.</p>
              {showMutationActions && (
                <button type="button" className="btn btn-primary btn-sm" onClick={() => toggleAction('followup')}>
                  إضافة أول تعقيب
                </button>
              )}
            </div>
          )}
          {!followUpsLoading && !followUpsError && followUps.length > 0 && (
            <div className="table-wrapper section-data-list">
              <table className="data-table data-table-compact">
                <thead><tr><th>الرقم</th><th>التاريخ</th><th>مرسل إلى</th><th>الرد</th><th>إجراء</th></tr></thead>
                <tbody>
                  {followUps.map((f) => (
                    <tr key={f.id}>
                      <td>{f.followUpNumber || '—'}</td>
                      <td><DateDisplay date={f.followUpDate} /></td>
                      <td>{f.departments?.length > 0 ? f.departments.map((d) => d.departmentName).join('، ') : f.sentTo || '—'}</td>
                      <td>
                        <span className={`badge ${f.replyStatus === 'Replied' ? 'badge-green' : 'badge-orange'}`}>
                          {replyStatusLabels[f.replyStatus] || f.replyStatus}
                        </span>
                      </td>
                      <td>
                        {f.requiresReply && f.replyStatus !== 'Replied' && canReply && (
                          <button
                            type="button"
                            className="btn btn-sm btn-outline"
                            onClick={() => openAction('reply-followup', { replyFollowUpId: f.id })}
                          >
                            تسجيل رد
                          </button>
                        )}
                        {f.replySummary && <div className="text-muted reply-summary">{f.replySummary}</div>}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </section>
      </div>

      <section className="card transaction-section-card transaction-section-card-full" aria-label="المرفقات">
        <div className="section-card-header">
          <div className="section-card-title">
            <span className="section-card-icon" aria-hidden>📎</span>
            <h3>المرفقات</h3>
            <span className="section-card-count">{attachments.length} مرفق</span>
          </div>
          {showMutationActions && (
            <button
              type="button"
              className={`btn btn-secondary btn-sm${activeAction === 'attachment' ? ' active' : ''}`}
              aria-pressed={activeAction === 'attachment'}
              onClick={() => toggleAction('attachment')}
            >
              + إضافة مرفق
            </button>
          )}
        </div>

        {activeAction === 'attachment' && (
          <CardActionPanel
            title={ACTION_TITLES.attachment}
            onClose={closeAction}
            testId="attachment-form-panel"
          >
            <AttachmentFormPanel
              transactionId={+id}
              onDirtyChange={setActionDirty}
              onCancel={closeAction}
              onSuccess={handleAttachmentSuccess}
            />
          </CardActionPanel>
        )}

        {attachmentsLoading && <LoadingInline label="جاري تحميل المرفقات..." />}
        {attachmentsError && (
          <Alert variant="error">
            {attachmentsError}
            <button type="button" className="btn btn-sm btn-outline ms-2" onClick={loadAttachments}>
              إعادة المحاولة
            </button>
          </Alert>
        )}
        {!attachmentsLoading && !attachmentsError && attachments.length === 0 && (
          <div className="section-empty-state">
            <p>لا توجد مرفقات لهذه المعاملة.</p>
            {showMutationActions && (
              <button type="button" className="btn btn-primary btn-sm" onClick={() => toggleAction('attachment')}>
                إضافة أول مرفق
              </button>
            )}
          </div>
        )}
        {!attachmentsLoading && !attachmentsError && attachments.length > 0 && (
          <div className="attachment-list section-data-list">
            {attachments.map((a) => (
              <article key={a.id} className="attachment-row-card">
                <div className="attachment-row-main">
                  <strong>{a.originalFileName}</strong>
                  <span className="text-muted">{(a.fileSize / 1024).toFixed(1)} KB</span>
                </div>
                <div className="attachment-row-meta text-muted">
                  {a.uploadedByName} • <DateDisplay date={a.uploadedAt} />
                </div>
                <div className="attachment-row-actions">
                  <button type="button" className="btn btn-sm btn-outline" onClick={() => downloadAttachment(a.id, a.originalFileName)}>
                    تحميل
                  </button>
                  {isPreviewableAttachment(a.contentType) && (
                    <button type="button" className="btn btn-sm btn-outline" onClick={() => previewAttachment(a.id, a.contentType)}>
                      معاينة
                    </button>
                  )}
                </div>
              </article>
            ))}
          </div>
        )}
      </section>
    </div>
  );

  return (
    <div className="transaction-workspace">
      <PageHeader title="مساحة عمل المعاملة" titleDescribedBy="transaction-context" />

      <div id="transaction-context" className="transaction-context-bar">
        <span className="transaction-context-number">{tx.incomingNumber}</span>
        <span aria-hidden="true">—</span>
        <span className="transaction-context-subject">{tx.subject}</span>
      </div>

      {message && <Alert variant="success">{message}</Alert>}
      {error && <Alert variant="error">{error}</Alert>}

      <div className="tabs" role="tablist" aria-label="تبويبات تفاصيل المعاملة">
        <button
          type="button"
          role="tab"
          aria-selected={activeTab === 'details'}
          className={activeTab === 'details' ? 'active' : ''}
          onClick={() => handleSelectTab('details')}
        >
          تفاصيل المعاملة
        </button>
        <button
          type="button"
          role="tab"
          aria-selected={activeTab === 'timeline'}
          className={activeTab === 'timeline' ? 'active' : ''}
          onClick={() => handleSelectTab('timeline')}
        >
          الخط الزمني
        </button>
        <button
          type="button"
          role="tab"
          aria-selected={activeTab === 'audit'}
          className={activeTab === 'audit' ? 'active' : ''}
          onClick={() => handleSelectTab('audit')}
        >
          سجل التدقيق
        </button>
      </div>

      {activeTab === 'details' && detailsTabContent}

      {activeTab === 'timeline' && (
        <TimelineAuditTabPanel
          mode="timeline"
          auditTabLoading={auditTabLoading}
          auditTabError={auditTabError}
          auditErrorTitle={auditErrorTitle}
          timelineEvents={timelineEvents}
          auditLogs={auditLogs}
          auditHasMore={auditHasMore}
          auditLoadingMore={auditLoadingMore}
          auditPage={auditPage}
          onRetry={loadAuditTabData}
          onLoadMore={(page) => loadAuditLog(page, true)}
        />
      )}

      {activeTab === 'audit' && (
        <TimelineAuditTabPanel
          mode="audit"
          auditTabLoading={auditTabLoading}
          auditTabError={auditTabError}
          auditErrorTitle={auditErrorTitle}
          timelineEvents={timelineEvents}
          auditLogs={auditLogs}
          auditHasMore={auditHasMore}
          auditLoadingMore={auditLoadingMore}
          auditPage={auditPage}
          onRetry={loadAuditTabData}
          onLoadMore={(page) => loadAuditLog(page, true)}
        />
      )}
    </div>
  );
}

type TimelineAuditTabPanelProps = Readonly<{
  mode: 'timeline' | 'audit';
  auditTabLoading: boolean;
  auditTabError: string;
  auditErrorTitle: string;
  timelineEvents: TimelineEvent[];
  auditLogs: AuditLog[];
  auditHasMore: boolean;
  auditLoadingMore: boolean;
  auditPage: number;
  onRetry: () => void;
  onLoadMore: (page: number) => void;
}>;

function TimelineAuditTabPanel({
  mode,
  auditTabLoading,
  auditTabError,
  auditErrorTitle,
  timelineEvents,
  auditLogs,
  auditHasMore,
  auditLoadingMore,
  auditPage,
  onRetry,
  onLoadMore,
}: TimelineAuditTabPanelProps) {
  const loadingLabel = mode === 'timeline' ? 'جاري تحميل الخط الزمني...' : 'جاري تحميل سجل التدقيق...';

  if (auditTabLoading) return <LoadingInline label={loadingLabel} />;

  if (auditTabError) {
    return (
      <ErrorState
        title={auditErrorTitle}
        description={auditTabError}
        action={(
          <button type="button" className="btn btn-primary" onClick={onRetry}>
            إعادة المحاولة
          </button>
        )}
      />
    );
  }

  if (mode === 'timeline') {
    return (
      <div className="card">
        <div className="card-header"><h3 className="card-title">الخط الزمني</h3></div>
        <ActivityTimeline events={timelineEvents} emptyLabel="لا توجد أحداث مسجلة" />
        {auditHasMore && (
          <div className="mt-4">
            <button
              type="button"
              className="btn btn-secondary btn-sm"
              disabled={auditLoadingMore}
              onClick={() => onLoadMore(auditPage + 1)}
            >
              {auditLoadingMore ? 'جاري التحميل...' : 'تحميل المزيد'}
            </button>
          </div>
        )}
      </div>
    );
  }

  return (
    <div className="card">
      <div className="card-header"><h3 className="card-title">سجل التدقيق</h3></div>
      <div className="table-wrapper">
        <table className="data-table">
          <thead><tr><th>الإجراء</th><th>المستخدم</th><th>التاريخ</th><th>التفاصيل</th></tr></thead>
          <tbody>
            {auditLogs.map((log) => (
              <tr key={log.id}>
                <td>{auditActionLabels[log.action] || log.action}</td>
                <td>{log.userName}</td>
                <td><DateDisplay date={log.createdAt} /></td>
                <td>{log.newValue || log.oldValue || '—'}</td>
              </tr>
            ))}
            {auditLogs.length === 0 && <tr><td colSpan={4} className="text-center">لا توجد سجلات</td></tr>}
          </tbody>
        </table>
      </div>
      {auditHasMore && (
        <div className="mt-2">
          <button
            type="button"
            className="btn btn-secondary"
            disabled={auditLoadingMore}
            onClick={() => onLoadMore(auditPage + 1)}
          >
            {auditLoadingMore ? 'جاري التحميل...' : 'تحميل المزيد'}
          </button>
        </div>
      )}
    </div>
  );
}
