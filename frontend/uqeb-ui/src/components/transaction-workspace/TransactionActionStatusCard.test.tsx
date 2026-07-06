import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import TransactionActionStatusCard from './TransactionActionStatusCard';
import type { TransactionDetail } from '../../api/types';

const baseTx: TransactionDetail = {
  id: 1,
  internalTrackingNumber: 'TRK-1',
  incomingNumber: 'IN-1',
  incomingDate: '2026-01-01',
  subject: 'موضوع',
  incomingSourceType: 'External',
  outgoingPartyNames: [],
  outgoingDepartmentNames: [],
  status: 'Open',
  priority: 'Normal',
  requiresResponse: true,
  responseCompleted: false,
  isOverdue: false,
  isResponseOverdue: false,
  hasPendingAssignments: false,
  isArchived: false,
  createdByName: 'مختبر',
  createdAt: '2026-01-01',
  responseType: 'Internal',
  outgoingParties: [],
  outgoingDepartments: [],
  repliedDepartmentNames: [],
  pendingDepartmentNames: [],
  followUps: [],
  assignments: [],
  attachments: [],
  auditLogs: [],
};

describe('TransactionActionStatusCard', () => {
  it('shows the pending-department names and a required-response message for admins', () => {
    render(
      <TransactionActionStatusCard
        tx={{ ...baseTx, pendingDepartmentNames: ['إدارة أ', 'إدارة ب'] }}
        needsResponse
        isTerminal={false}
        isDepartmentUser={false}
        canRegisterResponse={false}
      />,
    );

    expect(screen.getByRole('heading', { name: 'حالة الإجراء الحالية' })).toBeInTheDocument();
    expect(screen.getByText('بانتظار رد الإدارات المكلفة قبل تسجيل الإفادة.')).toBeInTheDocument();
    expect(screen.getByText(/إدارة أ، إدارة ب/)).toBeInTheDocument();
    expect(screen.getByText('لم تُسجَّل أي إفادة بعد.')).toBeInTheDocument();
  });

  it('tells the department user their response is required when they can act', () => {
    render(
      <TransactionActionStatusCard
        tx={baseTx}
        needsResponse
        isTerminal={false}
        isDepartmentUser
        canRegisterResponse
      />,
    );

    expect(screen.getByText('المطلوب من إدارتكم: تسجيل الإفادة.')).toBeInTheDocument();
  });

  it('shows the department review status when the department cannot act further', () => {
    render(
      <TransactionActionStatusCard
        tx={baseTx}
        needsResponse
        isTerminal={false}
        isDepartmentUser
        canRegisterResponse={false}
        departmentResponseActionStatusLabel="قيد المراجعة"
      />,
    );

    expect(screen.getByText('إفادة إدارتكم قيد المراجعة — بانتظار المراجعة.')).toBeInTheDocument();
  });

  it('shows a truncated latest response summary with a link into the responses section', () => {
    render(
      <TransactionActionStatusCard
        tx={{
          ...baseTx,
          responseCompleted: true,
          responseCompletedDate: '2026-02-01',
          responseSummary: 'تم الرد على المعاملة بشكل كامل.',
        }}
        needsResponse
        isTerminal={false}
        isDepartmentUser={false}
        canRegisterResponse={false}
      />,
    );

    expect(screen.getByText(/تم الرد على المعاملة بشكل كامل\./)).toBeInTheDocument();
    expect(screen.getByRole('link', { name: 'عرض التفاصيل' })).toHaveAttribute('href', '#transaction-responses-section');
  });

  it('says no action is required once the transaction is closed', () => {
    render(
      <TransactionActionStatusCard
        tx={baseTx}
        needsResponse
        isTerminal
        isDepartmentUser={false}
        canRegisterResponse={false}
      />,
    );

    expect(screen.getByText('تم إغلاق المعاملة. لا يوجد إجراء مطلوب حاليًا.')).toBeInTheDocument();
  });
});
