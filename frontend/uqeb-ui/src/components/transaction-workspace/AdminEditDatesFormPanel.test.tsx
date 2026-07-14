import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
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
    closedAt: null,
    completionDate: null,
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

  it('uses raw closedAt instead of computed completionDate for the close date field', () => {
    render(
      <AdminEditDatesFormPanel
        transactionId={1}
        transaction={{
          ...transaction,
          closedAt: '2026-06-01T00:00:00',
          completionDate: '2026-07-14T00:00:00',
        }}
        onDirtyChange={vi.fn()}
        onCancel={vi.fn()}
        onSuccess={vi.fn()}
      />,
    );

    expect(screen.getByLabelText('تاريخ إغلاق المعاملة - اختيار من التقويم')).toHaveValue('2026-06-01');
  });

  it('clears stale response due date and days when incoming date is cleared', async () => {
    mockApi(transactionsApi.adminEditTransactionDates).mockResolvedValue({ data: transaction });
    const user = userEvent.setup();

    render(
      <AdminEditDatesFormPanel
        transactionId={1}
        transaction={transaction}
        onDirtyChange={vi.fn()}
        onCancel={vi.fn()}
        onSuccess={vi.fn()}
      />,
    );

    await user.clear(screen.getByLabelText('تاريخ الوارد - اختيار من التقويم'));

    expect(screen.getByLabelText('تاريخ استحقاق المعاملة - اختيار من التقويم')).toHaveValue('');
    expect(screen.getByLabelText('عدد أيام الرد')).toHaveValue(null);

    await user.type(screen.getByLabelText(/سبب التعديل/), 'مسح تاريخ الوارد');
    await user.click(screen.getByRole('button', { name: 'حفظ التصحيح' }));

    await waitFor(() => expect(transactionsApi.adminEditTransactionDates).toHaveBeenCalledWith(1, expect.objectContaining({
      incomingDate: null,
      responseDueDate: null,
      responseDueDays: null,
    })));
  });

  it('does not populate NaN response due date for a negative days value', () => {
    render(
      <AdminEditDatesFormPanel
        transactionId={1}
        transaction={transaction}
        onDirtyChange={vi.fn()}
        onCancel={vi.fn()}
        onSuccess={vi.fn()}
      />,
    );

    const daysInput = screen.getByLabelText('عدد أيام الرد') as HTMLInputElement;
    fireEvent.change(daysInput, { target: { value: '-5' } });

    const dueDateInput = screen.getByLabelText('تاريخ استحقاق المعاملة - اختيار من التقويم') as HTMLInputElement;
    expect(dueDateInput.value).not.toContain('NaN');
  });

  it('rejects a negative response due days value on submit', async () => {
    const user = userEvent.setup();

    const { container } = render(
      <AdminEditDatesFormPanel
        transactionId={1}
        transaction={transaction}
        onDirtyChange={vi.fn()}
        onCancel={vi.fn()}
        onSuccess={vi.fn()}
      />,
    );

    await user.type(screen.getByLabelText(/سبب التعديل/), 'تصحيح');

    const daysInput = screen.getByLabelText('عدد أيام الرد');
    fireEvent.change(daysInput, { target: { value: '-5' } });

    // The native `min="0"` attribute would otherwise block a real button click
    // from ever dispatching the submit event; fire it directly to exercise the
    // JS-level guard that also rejects non-finite/negative values explicitly.
    fireEvent.submit(container.querySelector('form')!);

    await waitFor(() => {
      expect(screen.getByText('عدد أيام الرد لا يمكن أن يكون سالبًا.')).toBeInTheDocument();
    });
    expect(transactionsApi.adminEditTransactionDates).not.toHaveBeenCalled();
  });
});
