import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import AdminEditTransactionResponseFormPanel from './AdminEditTransactionResponseFormPanel';
import * as services from '../../api/services';
import type { TransactionDetail } from '../../api/types';

vi.mock('../../api/services', () => ({
  transactionsApi: {
    editResponse: vi.fn(),
  },
}));

function buildTransaction(overrides: Partial<TransactionDetail>): TransactionDetail {
  return {
    id: 1,
    internalTrackingNumber: 'TRK-1',
    incomingNumber: 'IN-1',
    incomingDate: '2026-01-01',
    subject: 'موضوع',
    incomingSourceType: 'External',
    incomingFrom: 'جهة',
    outgoingDepartmentNames: [],
    status: 'ResponseCompleted',
    isOverdue: false,
    requiresResponse: true,
    responseType: 'External',
    responseCompleted: true,
    priority: 'Normal',
    pendingDepartmentNames: [],
    repliedDepartmentNames: [],
    hasPendingAssignments: false,
    daysSinceIncoming: 1,
    daysSinceLastFollowUp: null,
    completionDate: null,
    completionDays: null,
    outgoingParties: [],
    outgoingDepartments: [],
    followUps: [],
    assignments: [],
    attachments: [],
    auditLogs: [],
    ...overrides,
  } as unknown as TransactionDetail;
}

function renderPanel(transaction: TransactionDetail) {
  return render(
    <AdminEditTransactionResponseFormPanel
      transactionId={1}
      transaction={transaction}
      onDirtyChange={vi.fn()}
      onCancel={vi.fn()}
      onSuccess={vi.fn()}
    />,
  );
}

describe('AdminEditTransactionResponseFormPanel', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  afterEach(() => {
    cleanup();
  });

  it('does not render or require outgoing fields when the response type does not require outgoing details', async () => {
    const user = userEvent.setup();
    vi.mocked(services.transactionsApi.editResponse).mockResolvedValue({ data: {} } as never);
    const tx = buildTransaction({
      responseType: 'Internal',
      responseCompletedDate: '2026-01-10',
      responseSummary: 'ملخص أصلي',
    });

    renderPanel(tx);

    expect(screen.queryByLabelText('رقم الصادر *')).not.toBeInTheDocument();
    expect(screen.queryByLabelText('تاريخ الصادر')).not.toBeInTheDocument();

    const summaryField = screen.getByLabelText('ملخص الإفادة *');
    await user.clear(summaryField);
    await user.type(summaryField, 'ملخص محدث');
    await user.click(screen.getByRole('button', { name: 'حفظ التعديلات' }));

    await waitFor(() => expect(services.transactionsApi.editResponse).toHaveBeenCalledTimes(1));
    expect(screen.queryByText(/لا يمكن أن يكون التاريخ بعد تاريخ اليوم/)).not.toBeInTheDocument();
  });

  it('does not block submission on a stale future outgoing date when outgoing is not required', async () => {
    const user = userEvent.setup();
    vi.mocked(services.transactionsApi.editResponse).mockResolvedValue({ data: {} } as never);
    const farFuture = new Date();
    farFuture.setFullYear(farFuture.getFullYear() + 5);
    const tx = buildTransaction({
      responseType: 'Internal',
      responseCompletedDate: '2026-01-10',
      responseSummary: 'ملخص أصلي',
      outgoingDate: farFuture.toISOString(),
      outgoingNumber: 'STALE-1',
    });

    renderPanel(tx);

    await user.type(screen.getByLabelText('ملخص الإفادة *'), ' إضافة');
    await user.click(screen.getByRole('button', { name: 'حفظ التعديلات' }));

    expect(screen.queryByText(/لا يمكن أن يكون التاريخ بعد تاريخ اليوم/)).not.toBeInTheDocument();
    await waitFor(() => expect(services.transactionsApi.editResponse).toHaveBeenCalledTimes(1));
  });

  it('requires and validates outgoing fields when the response type requires outgoing details', async () => {
    const user = userEvent.setup();
    const tx = buildTransaction({
      responseType: 'External',
      responseCompletedDate: '2026-01-10',
      responseSummary: 'ملخص أصلي',
      outgoingNumber: 'OUT-1',
      outgoingDate: '2026-01-10',
    });
    const updatedTx = { ...tx, responseSummary: 'ملخص أصلي إضافة' };
    vi.mocked(services.transactionsApi.editResponse).mockResolvedValue({ data: updatedTx } as never);
    const onSuccess = vi.fn();

    render(
      <AdminEditTransactionResponseFormPanel
        transactionId={1}
        transaction={tx}
        onDirtyChange={vi.fn()}
        onCancel={vi.fn()}
        onSuccess={onSuccess}
      />,
    );

    expect(screen.getByLabelText('رقم الصادر *')).toBeInTheDocument();
    const summaryField = screen.getByLabelText('ملخص الإفادة *');
    await user.type(summaryField, ' إضافة');
    await user.click(screen.getByRole('button', { name: 'حفظ التعديلات' }));

    await waitFor(() => expect(services.transactionsApi.editResponse).toHaveBeenCalledTimes(1));
    expect(services.transactionsApi.editResponse).toHaveBeenCalledWith(1, expect.objectContaining({
      responseSummary: 'ملخص أصلي إضافة',
      outgoingNumber: 'OUT-1',
    }));
    await waitFor(() => expect(onSuccess).toHaveBeenCalledWith(updatedTx));
    expect(screen.queryByText('تعذر حفظ التعديلات')).not.toBeInTheDocument();
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  });
});
