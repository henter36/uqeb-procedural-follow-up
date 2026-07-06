import {
  useCallback, useEffect, useRef, useState,
} from 'react';
import { Link, useParams, useNavigate, useSearchParams } from 'react-router-dom';
import { isAxiosError } from 'axios';
import { departmentResponsesApi, recurringTemplatesApi, transactionsApi } from '../api/services';
import type {
  TransactionDetail, Assignment, FollowUp, Attachment, AuditLog, DepartmentTransactionResponseItemDto,
} from '../api/types';
import { useAuth } from '../context/useAuth';
import { useReferenceData } from '../hooks/useReferenceData';
import { auditActionLabels } from '../utils/labels';
import { getApiErrorMessage } from '../utils/apiHelpers';
import DateDisplay from '../components/DateDisplay';
import {
  PageHeader, Alert, ActivityTimeline, LoadingInline, ErrorState,
} from '../components/ui';
import type { TimelineEvent } from '../components/ui';
import TransactionWorkspaceHeader from '../components/transaction-workspace/TransactionWorkspaceHeader';
import TransactionActionStatusCard from '../components/transaction-workspace/TransactionActionStatusCard';
import TransactionReferralsSection from '../components/transaction-workspace/TransactionReferralsSection';
import TransactionResponsesSection from '../components/transaction-workspace/TransactionResponsesSection';
import TransactionFollowUpsSection from '../components/transaction-workspace/TransactionFollowUpsSection';
import TransactionAttachmentsSection from '../components/transaction-workspace/TransactionAttachmentsSection';
import TransactionActionBar from '../components/transaction-workspace/TransactionActionBar';
import CardActionPanel from '../components/transaction-workspace/CardActionPanel';
import AdminEditDatesFormPanel from '../components/transaction-workspace/AdminEditDatesFormPanel';
import EnableRecurringFormPanel from '../components/transaction-workspace/EnableRecurringFormPanel';
import { departmentResponseStatusLabels } from '../components/transaction-workspace/departmentResponseStatusLabels';
import type { WorkspaceAction, WorkspaceActionContext } from '../components/transaction-workspace/types';
import { parseDetailTab, type DetailTab } from './transactionDetailTabs';

function isPreviewableAttachment(contentType?: string): boolean {
  if (!contentType) return false;
  return contentType.startsWith('image/') || contentType === 'application/pdf';
}

function hasDepartmentResponseAssignment(items: Assignment[], departmentId?: number | null): boolean {
  if (!departmentId) return false;
  return items.some(
    (item) => item.departmentId === departmentId && item.requiresReply && item.status !== 'Cancelled',
  );
}

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
  const [workspaceAgeDays, setWorkspaceAgeDays] = useState<number | null>(null);
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
  const [recurringSuggestion, setRecurringSuggestion] = useState<{ templateId: number; periodLabel: string } | null>(null);
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
    setWorkspaceAgeDays(data.temporalFacts?.ageDays ?? data.transaction.daysSinceIncoming ?? null);
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

  const handleEnableRecurringSuccess = (updated: import('../api/types').TransactionDetail) => {
    setTx(updated);
    closeAction();
    setMessage('تم تفعيل المتابعة الدورية لهذه المعاملة.');
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
      await checkRecurringSuggestionAfterClose(tx?.recurringTemplateId);
    } catch (err: unknown) {
      setError(getApiErrorMessage(err));
    }
  };

  const checkRecurringSuggestionAfterClose = async (recurringTemplateId?: number) => {
    if (!recurringTemplateId) return;
    try {
      const res = await recurringTemplatesApi.getById(recurringTemplateId);
      const template = res.data;
      if (template.status === 'Active' && template.nextTransactionCreationMethod === 'AutomaticOnClose') {
        setRecurringSuggestion({ templateId: template.id, periodLabel: template.nextPeriodLabel });
      }
    } catch {
      // Best-effort suggestion only; closing the transaction has already succeeded.
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
  const assignmentCardAgeText = workspaceAgeDays === null || workspaceAgeDays === undefined
    ? 'عمر المعاملة: غير متاح'
    : `عمر المعاملة: ${workspaceAgeDays} يوم`;

  const replyAssignmentId = actionContext.replyAssignmentId;
  const replyFollowUpId = actionContext.replyFollowUpId;
  const adminEditAssignmentId = actionContext.adminEditAssignmentId;
  const adminEditResponseId = actionContext.adminEditResponseId;

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
      <TransactionWorkspaceHeader
        tx={tx}
        assignments={assignments}
        attachmentsCount={attachments.length}
        actionsSlot={(
          <>
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
          </>
        )}
      >
        {tx.recurringTemplateId && (
          <div className="recurring-template-info-bar" data-testid="recurring-template-info">
            <span className="badge badge-blue">معاملة دورية</span>
            <span>القالب: {tx.recurringTemplateTitle}</span>
            <span className="transaction-hero-meta-sep">•</span>
            <span>الفترة: {tx.recurringPeriodLabel}</span>
            <span className="transaction-hero-meta-sep">•</span>
            <Link to={`/recurring-transaction-templates?highlight=${tx.recurringTemplateId}`}>الرجوع إلى القالب</Link>
            <span className="transaction-hero-meta-sep">•</span>
            <Link to={`/recurring-transaction-templates?viewTransactions=${tx.recurringTemplateId}`}>عرض معاملات نفس القالب</Link>
          </div>
        )}

        {recurringSuggestion && (
          <Alert variant="info">
            تم إغلاق المعاملة. هل تريد إنشاء معاملة الفترة القادمة ({recurringSuggestion.periodLabel}) الآن؟{' '}
            <Link to={`/recurring-transaction-templates?generate=${recurringSuggestion.templateId}`}>
              إنشاء معاملة الفترة القادمة
            </Link>
          </Alert>
        )}

        {canEdit && !tx?.recurringTemplateId && (
          <div className="admin-dates-edit-bar">
            <button
              type="button"
              className={`btn btn-sm btn-outline${activeAction === 'enable-recurring' ? ' active' : ''}`}
              aria-pressed={activeAction === 'enable-recurring'}
              onClick={() => toggleAction('enable-recurring')}
            >
              تفعيل متابعة دورية لهذه المعاملة
            </button>
          </div>
        )}

        {activeAction === 'enable-recurring' && (
          <CardActionPanel
            title="تفعيل متابعة دورية"
            onClose={closeAction}
            testId="enable-recurring-form-panel"
          >
            <EnableRecurringFormPanel
              transactionId={+id}
              incomingDate={tx.incomingDate}
              onDirtyChange={setActionDirty}
              onCancel={closeAction}
              onSuccess={handleEnableRecurringSuccess}
            />
          </CardActionPanel>
        )}

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
            title="تصحيح التواريخ الحساسة (إداري)"
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
            {tx.notes && <div className="full-width"><strong>ملاحظات:</strong> {tx.notes}</div>}
          </div>
        </details>
      </TransactionWorkspaceHeader>

      <TransactionActionStatusCard
        tx={tx}
        needsResponse={needsResponse}
        isTerminal={isTerminal}
        isDepartmentUser={isDepartmentUser}
        canRegisterResponse={canRegisterResponse}
        departmentResponseActionStatusLabel={departmentResponseActionStatusLabel}
      />

      <TransactionReferralsSection
        transactionId={id}
        assignments={assignments}
        assignmentsLoading={assignmentsLoading}
        assignmentsError={assignmentsError}
        onRetryLoad={loadAssignments}
        ageText={assignmentCardAgeText}
        departments={departments}
        fallbackLetterNumber={tx.outgoingNumber}
        showMutationActions={showMutationActions}
        canReply={canReply}
        isAdmin={isAdmin}
        activeAction={activeAction}
        replyAssignmentId={replyAssignmentId}
        adminEditAssignmentId={adminEditAssignmentId}
        adminEditResponseId={adminEditResponseId}
        onToggleAction={toggleAction}
        onOpenAction={openAction}
        onCloseAction={closeAction}
        onDirtyChange={setActionDirty}
        onAssignmentSuccess={handleAssignmentSuccess}
        onReplyAssignmentSuccess={handleReplyAssignmentSuccess}
        onAdminEditAssignmentSuccess={handleAdminEditAssignmentSuccess}
        onAdminEditResponseSuccess={handleAdminEditResponseSuccess}
      />

      <TransactionResponsesSection
        transactionId={id}
        tx={tx}
        isDepartmentUser={isDepartmentUser}
        activeAction={activeAction}
        responseActionLabel={responseActionLabel}
        departmentResponseItem={departmentResponseItem}
        panelRef={responsePanelRef}
        onDirtyChange={setActionDirty}
        onCancel={closeAction}
        onMessage={(nextMessage) => {
          setMessage(nextMessage);
          setError('');
        }}
        onDepartmentResponseChanged={handleDepartmentResponseChanged}
        onCompleteResponseSuccess={handleCompleteResponseSuccess}
      />

      <TransactionFollowUpsSection
        transactionId={id}
        tx={tx}
        assignments={assignments}
        followUps={followUps}
        followUpsLoading={followUpsLoading}
        followUpsError={followUpsError}
        onRetryLoad={loadFollowUps}
        showMutationActions={showMutationActions}
        canReply={canReply}
        activeAction={activeAction}
        replyFollowUpId={replyFollowUpId}
        onToggleAction={toggleAction}
        onOpenAction={openAction}
        onCloseAction={closeAction}
        onDirtyChange={setActionDirty}
        onFollowUpSuccess={handleFollowUpSuccess}
        onReplyFollowUpSuccess={handleReplyFollowUpSuccess}
        onFollowUpLetterDownloaded={() => setMessage('تم تحميل خطاب التعقيب بنجاح.')}
      />

      <TransactionAttachmentsSection
        transactionId={id}
        attachments={attachments}
        attachmentsLoading={attachmentsLoading}
        attachmentsError={attachmentsError}
        onRetryLoad={loadAttachments}
        showMutationActions={showMutationActions}
        activeAction={activeAction}
        onToggleAction={toggleAction}
        onCloseAction={closeAction}
        onDirtyChange={setActionDirty}
        onAttachmentSuccess={handleAttachmentSuccess}
        onDownload={downloadAttachment}
        onPreview={previewAttachment}
      />
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
