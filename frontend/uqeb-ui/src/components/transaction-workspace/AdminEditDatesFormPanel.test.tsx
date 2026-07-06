import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { afterEach, describe, expect, it, vi } from 'vitest';
import AdminEditDatesFormPanel from './AdminEditDatesFormPanel';
import { transactionsApi } from '../../api/services';

vi.mock('../../api/services', () => ({
  transactionsApi: {
    adminEditTransactionDates: vi.fn(),
  },
}));

type MockedApiFunction = {
  mockResolvedValue(value: unknown): MockedApiFunction;
};

const mockApi = (fn: unknown) => fn as MockedApiFunction;

afterEach(() => {
  cleanup();
  vi.clearAllMocks();
});

describe('AdminEditDatesFormPanel', () => {
  const transaction = {
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
    responseType: 'External',
    responseDueDays: 5,
    responseDueDate: '2026-01-06',
    isOverdue: false,
    isResponseOverdue: false,
    hasPendingAssignments: false,
    isArchived: false,
    createdByName: 'مدير النظام',
    createdAt: '2026-01-01',
    outgoingParties: [],
    outgoingDepartments: [],
    repliedDepartmentNames: [],
    pendingDepartmentNames: [],
    followUps: [],
    assignments: [],
    attachments: [],
    auditLogs: [],
  };

  it('synchronizes due date edits into response due days and clears dirty after success', async () => {
    mockApi(transactionsApi.adminEditTransactionDates).mockResolvedValue({ data: transaction });
    const onDirtyChange = vi.fn();
    const onSuccess = vi.fn();
    const user = userEvent.setup();

    render(
      <AdminEditDatesFormPanel
        transactionId={1}
        transaction={transaction}
        onDirtyChange={onDirtyChange}
        onCancel={vi.fn()}
        onSuccess={onSuccess}
      />,
    );

    await user.clear(screen.getByLabelText('تاريخ استحقاق المعاملة - اختيار من التقويم'));
    await user.type(screen.getByLabelText('تاريخ استحقاق المعاملة - اختيار من التقويم'), '2026-01-10');
    await user.type(screen.getByLabelText(/سبب التعديل/), 'تصحيح التاريخ');

    expect(screen.getByLabelText('عدد أيام الرد')).toHaveValue(9);

    await user.click(screen.getByRole('button', { name: 'حفظ التصحيح' }));

    await waitFor(() => expect(transactionsApi.adminEditTransactionDates).toHaveBeenCalledWith(1, expect.objectContaining({
      responseDueDays: 9,
      responseDueDate: '2026-01-10',
    })));
    expect(onDirtyChange).toHaveBeenCalledWith(false);
    expect(onSuccess).toHaveBeenCalled();
  });
});
