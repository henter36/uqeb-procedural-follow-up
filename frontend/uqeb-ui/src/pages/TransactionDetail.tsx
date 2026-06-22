import {
  useCallback, useEffect, useRef, useState, type ReactNode,
} from 'react';
import { useParams, useNavigate, useSearchParams } from 'react-router-dom';
import { transactionsApi } from '../api/services';
import type {
  TransactionDetail, Assignment, FollowUp, Attachment, AuditLog,
} from '../api/types';
import { useAuth } from '../context/AuthContext';
import { useReferenceData } from '../context/ReferenceDataContext';
import {
  statusBadgeClass, responseTypeLabels,
  auditActionLabels, replyStatusLabels,
} from '../utils/labels';
import { getApiErrorMessage } from '../utils/apiHelpers';
import DateDisplay from '../components/DateDisplay';
import { responseTimingBadgeClass, formatDaysSince } from '../utils/responseTiming';
import {
  PageHeader, Alert, StatusBadge, PriorityBadge, ActivityTimeline, LoadingInline, ErrorState,
} from '../components/ui';
import type { TimelineEvent } from '../components/ui';
import TransactionWorkspaceHeader from '../components/transaction-workspace/TransactionWorkspaceHeader';
import TransactionActionBar from '../components/transaction-workspace/TransactionActionBar';
import TransactionActionPanel from '../components/transaction-workspace/TransactionActionPanel';
import AssignmentFormPanel from '../components/transaction-workspace/AssignmentFormPanel';
import FollowUpFormPanel from '../components/transaction-workspace/FollowUpFormPanel';
import AttachmentFormPanel from '../components/transaction-workspace/AttachmentFormPanel';
import ReplyFormPanel from '../components/transaction-workspace/ReplyFormPanel';
import CompleteResponseFormPanel from '../components/transaction-workspace/CompleteResponseFormPanel';
import FollowUpLetterFormPanel from '../components/transaction-workspace/FollowUpLetterFormPanel';
import type { WorkspaceAction, WorkspaceActionContext } from '../components/transaction-workspace/types';

type DetailTab = 'overview' | 'assignments' | 'followups' | 'attachments' | 'audit' | 'timeline';
type LazyTab = 'attachments' | 'audit';

function parseDetailTab(tabFromUrl: string | null): DetailTab {
  if (tabFromUrl === 'audit') return 'audit';
  if (tabFromUrl === 'attachments') return 'attachments';
  if (tabFromUrl === 'timeline') return 'timeline';
  return 'overview';
}

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

type InitialLazyTabHandlers = Readonly<{
  loadAttachmentsData: (isMounted: () => boolean) => Promise<void>;
  loadAuditData: (isMounted: () => boolean) => Promise<void>;
  onAttachmentsError: () => void;
  onAuditError: () => void;
  onTabLoadingComplete: () => void;
}>;

type LazyTabLoadPlan = Readonly<{
  load: (isMounted: () => boolean) => Promise<void>;
  onError: () => void;
}>;

function resolveInitialLazyTabPlan(
  tab: DetailTab,
  handlers: InitialLazyTabHandlers,
): LazyTabLoadPlan | null {
  if (tab === 'attachments') {
    return {
      load: handlers.loadAttachmentsData,
      onError: handlers.onAttachmentsError,
    };
  }
  if (tab === 'audit' || tab === 'timeline') {
    return {
      load: handlers.loadAuditData,
      onError: handlers.onAuditError,
    };
  }
  return null;
}

async function loadInitialLazyTab(
  tab: DetailTab,
  isMounted: () => boolean,
  handlers: InitialLazyTabHandlers,
): Promise<void> {
  const plan = resolveInitialLazyTabPlan(tab, handlers);
  if (!plan) return;

  try {
    await plan.load(isMounted);
  } catch {
    if (isMounted()) plan.onError();
  } finally {
    if (isMounted()) handlers.onTabLoadingComplete();
  }
}

function handleLazyTabLoadFailure(
  tab: LazyTab,
  isMounted: () => boolean,
  onAttachmentsFailure: () => void,
  onAuditFailure: () => void,
): void {
  if (!isMounted()) return;
  if (tab === 'attachments') onAttachmentsFailure();
  else onAuditFailure();
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
  const initialTabFromUrl = parseDetailTab(tabFromUrl);
  const { canEdit, canClose, isDepartmentUser } = useAuth();
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
  const [loadedTabs, setLoadedTabs] = useState<Record<LazyTab, boolean>>({
    attachments: false,
    audit: false,
  });
  const [attachmentsTabError, setAttachmentsTabError] = useState('');
  const [auditTabError, setAuditTabError] = useState('');
  const loadedTabsRef = useRef(loadedTabs);
  useEffect(() => {
    loadedTabsRef.current = loadedTabs;
  }, [loadedTabs]);
  const [tabLoading, setTabLoading] = useState(
    () => initialTabFromUrl === 'attachments' || initialTabFromUrl === 'audit' || initialTabFromUrl === 'timeline',
  );
  const [assignmentsLoading, setAssignmentsLoading] = useState(true);
  const [followUpsLoading, setFollowUpsLoading] = useState(true);
  const [assignmentsError, setAssignmentsError] = useState('');
  const [followUpsError, setFollowUpsError] = useState('');
  const [activeAction, setActiveAction] = useState<WorkspaceAction | null>(null);
  const [actionContext, setActionContext] = useState<WorkspaceActionContext>({});
  const [actionDirty, setActionDirty] = useState(false);
  const [message, setMessage] = useState('');
  const [error, setError] = useState('');

  const loadBasic = useCallback(() => {
    if (!id) return;
    transactionsApi.getBasic(+id).then((r) => setTx(r.data)).catch(() => navigate('/transactions'));
  }, [id, navigate]);

  const loadAssignments = useCallback(async () => {
    if (!id) return;
    setAssignmentsLoading(true);
    setAssignmentsError('');
    try {
      const res = await transactionsApi.getAssignments(+id);
      setAssignments(res.data ?? []);
    } catch {
      setAssignmentsError('تعذر تحميل التحويلات');
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

  const loadAuditLog = useCallback(async (page: number, append: boolean) => {
    if (!id) return;
    if (append) setAuditLoadingMore(true);
    else setAuditTabError('');
    try {
      const res = await transactionsApi.getAuditLog(+id, page);
      setAuditLogs((prev) => (append ? [...prev, ...res.data.items] : res.data.items));
      setAuditPage(page);
      setAuditHasMore(res.data.hasNextPage);
      if (!append) {
        setLoadedTabs((prev) => ({ ...prev, audit: true }));
      }
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

  const loadAttachmentsData = useCallback(async (isMounted: () => boolean) => {
    const res = await transactionsApi.getAttachments(+id);
    if (!isMounted()) return;
    setAttachments(res.data);
    setLoadedTabs((prev) => ({ ...prev, attachments: true }));
  }, [id]);

  const loadAuditData = useCallback(async (isMounted: () => boolean) => {
    const res = await transactionsApi.getAuditLog(+id, 1);
    if (!isMounted()) return;
    setAuditLogs(res.data.items);
    setAuditPage(1);
    setAuditHasMore(res.data.hasNextPage);
    setLoadedTabs((prev) => ({ ...prev, audit: true }));
  }, [id]);

  const loadTab = useCallback(async (
    tab: LazyTab,
    force = false,
    isMounted: () => boolean = () => true,
  ) => {
    if (!id || (!force && loadedTabsRef.current[tab])) return;
    setTabLoading(true);
    if (tab === 'attachments') setAttachmentsTabError('');
    else setAuditTabError('');

    try {
      if (tab === 'attachments') {
        await loadAttachmentsData(isMounted);
      } else {
        await loadAuditData(isMounted);
      }
    } catch {
      handleLazyTabLoadFailure(
        tab,
        isMounted,
        () => {
          setAttachmentsTabError('تعذر تحميل المرفقات');
          setAttachments([]);
        },
        () => {
          setAuditTabError('تعذر تحميل سجل التدقيق');
          setAuditLogs([]);
          setAuditHasMore(false);
        },
      );
    } finally {
      if (isMounted()) setTabLoading(false);
    }
  }, [id, loadAttachmentsData, loadAuditData]);

  const selectTab = (tab: DetailTab) => {
    setActiveTab(tab);
    if (tab === 'attachments' || tab === 'audit' || tab === 'timeline') {
      loadTab(tab === 'timeline' ? 'audit' : tab);
    }
  };

  useEffect(() => {
    let active = true;
    const isMounted = () => active;

    loadBasic();

    void (async () => {
      try {
        const res = await transactionsApi.getAssignments(+id);
        if (isMounted()) setAssignments(res.data ?? []);
      } catch {
        if (isMounted()) setAssignmentsError('تعذر تحميل التحويلات');
      } finally {
        if (isMounted()) setAssignmentsLoading(false);
      }
    })();

    void (async () => {
      try {
        const res = await transactionsApi.getFollowUps(+id);
        if (isMounted()) setFollowUps(res.data ?? []);
      } catch {
        if (isMounted()) setFollowUpsError('تعذر تحميل التعقيبات');
      } finally {
        if (isMounted()) setFollowUpsLoading(false);
      }
    })();

    void loadInitialLazyTab(initialTabFromUrl, isMounted, {
      loadAttachmentsData,
      loadAuditData,
      onAttachmentsError: () => {
        setAttachmentsTabError('تعذر تحميل المرفقات');
        setAttachments([]);
      },
      onAuditError: () => {
        setAuditTabError('تعذر تحميل سجل التدقيق');
        setAuditLogs([]);
        setAuditHasMore(false);
      },
      onTabLoadingComplete: () => setTabLoading(false),
    });

    return () => { active = false; };
  }, [id, initialTabFromUrl, loadBasic, loadAttachmentsData, loadAuditData]);

  const refreshTimelineIfLoaded = useCallback(() => {
    if (loadedTabsRef.current.audit) {
      void loadTab('audit', true);
    }
  }, [loadTab]);

  const closeAction = useCallback(() => {
    if (actionDirty && !globalThis.confirm('يوجد بيانات غير محفوظة. هل تريد إغلاق النموذج؟')) {
      return;
    }
    setActiveAction(null);
    setActionContext({});
    setActionDirty(false);
  }, [actionDirty]);

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

  const toggleAction = useCallback((action: WorkspaceAction) => {
    if (activeAction === action) {
      closeAction();
    } else {
      openAction(action);
    }
  }, [activeAction, closeAction, openAction]);

  const handleActionSuccess = useCallback((successMessage: string, refreshers: Array<() => void | Promise<void>>) => {
    setMessage(successMessage);
    setError('');
    closeAction();
    refreshers.forEach((fn) => { void fn(); });
    refreshTimelineIfLoaded();
  }, [closeAction, refreshTimelineIfLoaded]);

  const handleClose = async () => {
    if (!tx) return;
    const needsResponse = tx.requiresResponse || tx.responseType !== 'None';
    if (needsResponse && !tx.responseCompleted) {
      setError('لا يمكن إغلاق المعاملة قبل تسجيل الإفادة.');
      return;
    }
    if (!confirm('هل تريد إغلاق المعاملة؟')) return;
    setError('');
    try {
      await transactionsApi.close(+id!);
      loadBasic();
      loadAssignments();
      setMessage('تم إغلاق المعاملة');
    } catch (err: unknown) {
      setError(getApiErrorMessage(err));
    }
  };

  const refreshAttachments = () => {
    loadTab('attachments', true);
  };

  const downloadAttachment = async (attachmentId: number, fileName: string) => {
    const res = await transactionsApi.downloadAttachment(+id!, attachmentId);
    const url = window.URL.createObjectURL(res.data);
    const a = document.createElement('a');
    a.href = url; a.download = fileName; a.click();
  };

  if (!tx) return <div className="loading"><LoadingInline /></div>;

  const needsResponse = tx.requiresResponse || tx.responseType !== 'None';
  const isTerminal = tx.status === 'Closed' || tx.status === 'Cancelled' || tx.status === 'Archived';
  const hasPendingDepts = tx.pendingDepartmentNames.length > 0;
  const canRegisterResponse = canClose && needsResponse && !tx.responseCompleted && !isTerminal;
  const canShowClose = canClose && !isTerminal && (!needsResponse || tx.responseCompleted);

  const timelineEvents: TimelineEvent[] = auditLogs.map((log) => ({
    id: log.id,
    action: auditActionLabels[log.action] || log.action,
    userName: log.userName,
    date: log.createdAt,
    detail: log.newValue || log.oldValue || undefined,
  }));

  const overviewContent = (
    <>
      <div className={`status-bar badge ${statusBadgeClass(tx.status, tx.isOverdue)}`}>
        <strong>الحالة:</strong> <StatusBadge status={tx.status} isOverdue={tx.isOverdue} />
        {tx.isOverdue && <span className="status-extra"> — متأخرة</span>}
        {tx.hasPendingAssignments && <span className="status-extra"> — باقي إجراء</span>}
        {tx.responseTimingLabel && tx.requiresResponse && (
          <span className={`status-extra badge badge-spaced ${responseTimingBadgeClass(tx.responseTimingStatus)}`}>
            {tx.responseTimingLabel}
          </span>
        )}
      </div>

      <div className="timeline-stats mt-4">
        <div className="timeline-stat">
          <span className="timeline-stat-label">منذ ورود المعاملة</span>
          <span className="timeline-stat-value">{formatDaysSince(tx.daysSinceIncoming, '0')}</span>
        </div>
        <div className="timeline-stat">
          <span className="timeline-stat-label">منذ آخر تعقيب</span>
          <span className="timeline-stat-value">{formatDaysSince(tx.daysSinceLastFollowUp)}</span>
        </div>
        <div className="timeline-stat">
          <span className="timeline-stat-label">تاريخ الرد المطلوب</span>
          <span className="timeline-stat-value">
            {tx.responseDueDate ? <DateDisplay date={tx.responseDueDate} /> : '—'}
          </span>
        </div>
        <div className="timeline-stat">
          <span className="timeline-stat-label">الأولوية</span>
          <span className="timeline-stat-value"><PriorityBadge priority={tx.priority} /></span>
        </div>
      </div>

      <div className="card mt-4">
        <div className="detail-grid">
          <div><strong>رقم التتبع:</strong> {tx.internalTrackingNumber}</div>
          <div><strong>رقم الوارد:</strong> {tx.incomingNumber}</div>
          <div><strong>تاريخ الوارد:</strong> <DateDisplay date={tx.incomingDate} /></div>
          <div><strong>التصنيف:</strong> {tx.categoryName || '-'}</div>
          <div><strong>نوع الجهة الوارد منها:</strong> {tx.incomingSourceType === 'Internal' ? 'داخلية' : 'خارجية'}</div>
          <div><strong>الجهة الوارد منها:</strong> {tx.incomingFrom || '-'}</div>
          <div className="full-width"><strong>الموضوع:</strong> {tx.subject}</div>
          {tx.outgoingNumber && <div><strong>رقم الصادر:</strong> {tx.outgoingNumber}</div>}
          {tx.outgoingDate && <div><strong>تاريخ الصادر:</strong> <DateDisplay date={tx.outgoingDate} /></div>}
          <div className="full-width">
            <strong>الإدارات الصادر لها:</strong>{' '}
            {tx.outgoingDepartments.length > 0 ? tx.outgoingDepartments.map((o) => o.departmentName).join('، ') : '-'}
          </div>
          {needsResponse && (
            <>
              <div><strong>مطلوب إفادة:</strong> نعم ({responseTypeLabels[tx.responseType] || tx.responseType})</div>
              {tx.responseDueDate && <div><strong>تاريخ استحقاق الإفادة:</strong> <DateDisplay date={tx.responseDueDate} /></div>}
              <div>
                <strong>حالة الإفادة:</strong> {responseStatusLabel(tx.responseCompleted, tx.responseCompletedDate)}
              </div>
              {tx.responseSummary && <div className="full-width"><strong>ملخص الإفادة:</strong> {tx.responseSummary}</div>}
              {hasPendingDepts && !tx.responseCompleted && (
                <div className="full-width overdue-text">لا يمكن تسجيل الإفادة قبل اكتمال رد جميع الإدارات.</div>
              )}
            </>
          )}
          {tx.repliedDepartmentNames.length > 0 && (
            <div className="full-width"><strong>الإدارات التي ردت:</strong> {tx.repliedDepartmentNames.join('، ')}</div>
          )}
          {tx.pendingDepartmentNames.length > 0 && (
            <div className="full-width overdue-text"><strong>الإدارات التي لم ترد:</strong> {tx.pendingDepartmentNames.join('، ')}</div>
          )}
          {tx.notes && <div className="full-width"><strong>ملاحظات:</strong> {tx.notes}</div>}
        </div>
      </div>
    </>
  );

  const assignmentsContent = (
    <div className="card">
      <div className="card-header">
        <h3>التحويلات والردود</h3>
      </div>
      {assignmentsLoading && <LoadingInline label="جاري تحميل التحويلات..." />}
      {assignmentsError && <Alert variant="error">{assignmentsError}</Alert>}
      {!assignmentsLoading && !assignmentsError && (
        <div className="table-wrapper">
          <table className="data-table">
            <thead><tr><th>الإدارة</th><th>الإجراء</th><th>تاريخ الاستحقاق</th><th>حالة الرد</th><th>إجراء</th></tr></thead>
            <tbody>
              {assignments.map((a) => (
                <tr key={a.id} className={a.isOverdue ? 'row-overdue' : ''}>
                  <td>{a.departmentName}</td>
                  <td>{a.requiredAction || '-'}</td>
                  <td>{a.dueDate ? <DateDisplay date={a.dueDate} /> : '-'}</td>
                  <td>
                    <span className={`badge ${assignmentReplyBadgeClass(a.replyStatus, a.isOverdue)}`}>
                      {replyStatusLabels[a.replyStatus] || a.replyStatus}
                    </span>
                  </td>
                  <td>
                    {a.requiresReply && a.replyStatus !== 'Replied' && a.status !== 'Cancelled' && (isDepartmentUser || canEdit) && (
                      <button
                        type="button"
                        className="btn btn-sm btn-outline"
                        onClick={() => openAction('reply-assignment', { replyAssignmentId: a.id })}
                      >
                        تسجيل رد
                      </button>
                    )}
                    {a.replySummary && <div className="text-muted reply-summary">{a.replySummary}</div>}
                  </td>
                </tr>
              ))}
              {assignments.length === 0 && <tr><td colSpan={5} className="text-center">لا توجد تحويلات</td></tr>}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );

  const followupsContent = (
    <div className="card">
      <div className="card-header">
        <h3>التعقيبات</h3>
      </div>
      {followUpsLoading && <LoadingInline label="جاري تحميل التعقيبات..." />}
      {followUpsError && <Alert variant="error">{followUpsError}</Alert>}
      {!followUpsLoading && !followUpsError && (
        <div className="table-wrapper">
          <table className="data-table">
            <thead><tr><th>الرقم</th><th>التاريخ</th><th>مرسل إلى</th><th>ملاحظات</th><th>حالة الرد</th><th>إجراء</th></tr></thead>
            <tbody>
              {followUps.map((f) => (
                <tr key={f.id}>
                  <td>{f.followUpNumber || '-'}</td>
                  <td><DateDisplay date={f.followUpDate} /></td>
                  <td>{f.departments?.length > 0 ? f.departments.map((d) => d.departmentName).join('، ') : f.sentTo || '-'}</td>
                  <td>{f.notes || '-'}</td>
                  <td>
                    <span className={`badge ${f.replyStatus === 'Replied' ? 'badge-green' : 'badge-orange'}`}>
                      {replyStatusLabels[f.replyStatus] || f.replyStatus}
                    </span>
                  </td>
                  <td>
                    {f.requiresReply && f.replyStatus !== 'Replied' && (isDepartmentUser || canEdit) && (
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
              {followUps.length === 0 && <tr><td colSpan={6} className="text-center">لا توجد تعقيبات</td></tr>}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );

  const existingDepartmentIds = assignments.map((a) => a.departmentId);

  const actionPanelTitle: Record<WorkspaceAction, string> = {
    assignment: 'إضافة تحويل',
    followup: 'إضافة تعقيب',
    attachment: 'إضافة مرفق',
    'reply-assignment': 'تسجيل رد على التحويل',
    'reply-followup': 'تسجيل رد على التعقيب',
    'complete-response': 'تسجيل الإفادة',
    'follow-up-letter': 'خطاب تعقيب PDF',
  };

  return (
    <div className="transaction-workspace">
      <PageHeader title="مساحة عمل المعاملة" subtitle={`${tx.incomingNumber} — ${tx.subject}`} />

      {message && <Alert variant="success">{message}</Alert>}
      {error && <Alert variant="error">{error}</Alert>}

      <TransactionWorkspaceHeader tx={tx} />

      <TransactionActionBar
        transactionId={id}
        canEdit={canEdit}
        canClose={canClose}
        isDepartmentUser={isDepartmentUser}
        canRegisterResponse={canRegisterResponse}
        canShowClose={canShowClose}
        hasPendingDepts={hasPendingDepts}
        activeAction={activeAction}
        onAction={toggleAction}
        onCloseTransaction={handleClose}
      />

      {activeAction && (
        <TransactionActionPanel
          title={actionPanelTitle[activeAction]}
          open
          dirty={actionDirty}
          onClose={closeAction}
        >
          {activeAction === 'assignment' && (
            <AssignmentFormPanel
              transactionId={+id}
              departments={departments}
              existingDepartmentIds={existingDepartmentIds}
              onDirtyChange={setActionDirty}
              onCancel={closeAction}
              onSuccess={() => handleActionSuccess('تم إضافة التحويل بنجاح.', [loadAssignments, loadBasic])}
            />
          )}
          {activeAction === 'followup' && (
            <FollowUpFormPanel
              transactionId={+id}
              daysSinceLastFollowUp={tx.daysSinceLastFollowUp}
              onDirtyChange={setActionDirty}
              onCancel={closeAction}
              onSuccess={() => handleActionSuccess('تم إضافة التعقيب بنجاح.', [loadFollowUps, loadBasic])}
            />
          )}
          {activeAction === 'attachment' && (
            <AttachmentFormPanel
              transactionId={+id}
              onDirtyChange={setActionDirty}
              onCancel={closeAction}
              onSuccess={() => handleActionSuccess('تم رفع المرفق بنجاح.', [refreshAttachments])}
            />
          )}
          {activeAction === 'reply-assignment' && actionContext.replyAssignmentId && (
            <ReplyFormPanel
              title="تسجيل رد على التحويل"
              onDirtyChange={setActionDirty}
              onCancel={closeAction}
              onSubmit={(payload) => transactionsApi.replyAssignment(+id, actionContext.replyAssignmentId!, payload)}
              onSuccess={() => handleActionSuccess('تم تسجيل الرد بنجاح.', [loadAssignments, loadBasic])}
            />
          )}
          {activeAction === 'reply-followup' && actionContext.replyFollowUpId && (
            <ReplyFormPanel
              title="تسجيل رد على التعقيب"
              onDirtyChange={setActionDirty}
              onCancel={closeAction}
              onSubmit={(payload) => transactionsApi.replyFollowUp(+id, actionContext.replyFollowUpId!, payload)}
              onSuccess={() => handleActionSuccess('تم تسجيل الرد بنجاح.', [loadFollowUps])}
            />
          )}
          {activeAction === 'complete-response' && (
            <CompleteResponseFormPanel
              transactionId={+id}
              responseType={tx.responseType}
              onDirtyChange={setActionDirty}
              onCancel={closeAction}
              onSuccess={() => handleActionSuccess('تم تسجيل الإفادة بنجاح.', [loadBasic, loadAssignments])}
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

      <div className="tabs" role="tablist" aria-label="تبويبات تفاصيل المعاملة">
        <button type="button" role="tab" aria-selected={activeTab === 'overview'} className={activeTab === 'overview' ? 'active' : ''} onClick={() => selectTab('overview')}>نظرة عامة</button>
        <button type="button" role="tab" aria-selected={activeTab === 'assignments'} className={activeTab === 'assignments' ? 'active' : ''} onClick={() => selectTab('assignments')}>
          التحويلات<span className="tab-count">{assignments.length}</span>
        </button>
        <button type="button" role="tab" aria-selected={activeTab === 'followups'} className={activeTab === 'followups' ? 'active' : ''} onClick={() => selectTab('followups')}>
          التعقيبات<span className="tab-count">{followUps.length}</span>
        </button>
        <button type="button" role="tab" aria-selected={activeTab === 'attachments'} className={activeTab === 'attachments' ? 'active' : ''} onClick={() => selectTab('attachments')}>
          المرفقات<span className="tab-count">{attachments.length}</span>
        </button>
        <button type="button" role="tab" aria-selected={activeTab === 'timeline'} className={activeTab === 'timeline' ? 'active' : ''} onClick={() => selectTab('timeline')}>السجل الزمني</button>
        <button type="button" role="tab" aria-selected={activeTab === 'audit'} className={activeTab === 'audit' ? 'active' : ''} onClick={() => selectTab('audit')}>سجل التدقيق</button>
      </div>

      {tabLoading && <LoadingInline label="جاري التحميل..." />}

      {activeTab === 'overview' && overviewContent}
      {activeTab === 'assignments' && assignmentsContent}
      {activeTab === 'followups' && followupsContent}

      {activeTab === 'attachments' && !tabLoading && attachmentsTabError && (
        <ErrorState
          title="تعذر تحميل المرفقات"
          description={attachmentsTabError}
          action={(
            <button type="button" className="btn btn-primary" onClick={() => loadTab('attachments', true)}>
              إعادة المحاولة
            </button>
          )}
        />
      )}

      {activeTab === 'attachments' && !tabLoading && !attachmentsTabError && (
        <div className="card mt-2">
          <div className="card-header">
            <h3>المرفقات</h3>
          </div>
          <div className="table-wrapper">
            <table className="data-table">
              <thead><tr><th>الملف</th><th>الحجم</th><th>رفع بواسطة</th><th>التاريخ</th><th>تحميل</th></tr></thead>
              <tbody>
                {attachments.map((a) => (
                  <tr key={a.id}>
                    <td>{a.originalFileName}</td>
                    <td>{(a.fileSize / 1024).toFixed(1)} KB</td>
                    <td>{a.uploadedByName}</td>
                    <td><DateDisplay date={a.uploadedAt} /></td>
                    <td><button type="button" className="btn btn-sm btn-outline" onClick={() => downloadAttachment(a.id, a.originalFileName)}>تحميل</button></td>
                  </tr>
                ))}
                {attachments.length === 0 && <tr><td colSpan={5} className="text-center">لا توجد مرفقات</td></tr>}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {activeTab === 'timeline' && !tabLoading && auditTabError && (
        <ErrorState
          title="تعذر تحميل السجل الزمني"
          description={auditTabError}
          action={(
            <button type="button" className="btn btn-primary" onClick={() => loadTab('audit', true)}>
              إعادة المحاولة
            </button>
          )}
        />
      )}

      {activeTab === 'timeline' && !tabLoading && !auditTabError && (
        <div className="card">
          <h3>السجل الزمني للمعاملة</h3>
          <ActivityTimeline events={timelineEvents} emptyLabel="لا توجد أحداث مسجلة" />
          {auditHasMore && (
            <div className="mt-4">
              <button
                type="button"
                className="btn btn-secondary btn-sm"
                disabled={auditLoadingMore}
                onClick={() => loadAuditLog(auditPage + 1, true)}
              >
                {auditLoadingMore ? 'جاري التحميل...' : 'تحميل المزيد'}
              </button>
            </div>
          )}
        </div>
      )}

      {activeTab === 'audit' && !tabLoading && auditTabError && (
        <ErrorState
          title="تعذر تحميل سجل التدقيق"
          description={auditTabError}
          action={(
            <button type="button" className="btn btn-primary" onClick={() => loadTab('audit', true)}>
              إعادة المحاولة
            </button>
          )}
        />
      )}

      {activeTab === 'audit' && !tabLoading && !auditTabError && (
        <div className="card">
          <h3>سجل التدقيق</h3>
          <div className="table-wrapper">
            <table className="data-table">
            <thead><tr><th>الإجراء</th><th>المستخدم</th><th>التاريخ</th><th>التفاصيل</th></tr></thead>
            <tbody>
              {auditLogs.map((log) => (
                <tr key={log.id}>
                  <td>{auditActionLabels[log.action] || log.action}</td>
                  <td>{log.userName}</td>
                  <td><DateDisplay date={log.createdAt} /></td>
                  <td>{log.newValue || log.oldValue || '-'}</td>
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
                onClick={() => loadAuditLog(auditPage + 1, true)}
              >
                {auditLoadingMore ? 'جاري التحميل...' : 'تحميل المزيد'}
              </button>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
