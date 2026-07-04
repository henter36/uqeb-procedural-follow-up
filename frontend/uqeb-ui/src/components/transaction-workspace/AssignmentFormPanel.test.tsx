import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import AssignmentFormPanel from './AssignmentFormPanel';
import * as services from '../../api/services';
import { addDaysIso, todayLocalIso } from '../../utils/localDate';

vi.mock('../../api/services', () => ({
  transactionsApi: {
    addAssignment: vi.fn(),
  },
}));

const departments = [{ id: 1, name: 'إدارة أ', isActive: true }];

describe('AssignmentFormPanel dirty state', () => {
  const onDirtyChange = vi.fn();
  const onSuccess = vi.fn();
  const onCancel = vi.fn();

  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(services.transactionsApi.addAssignment).mockResolvedValue({ data: { id: 1 } } as never);
  });

  afterEach(() => {
    cleanup();
  });

  it('is not dirty on first open', async () => {
    render(
      <AssignmentFormPanel
        transactionId={1}
        departments={departments}
        existingDepartmentIds={[]}
        onDirtyChange={onDirtyChange}
        onSuccess={onSuccess}
        onCancel={onCancel}
      />,
    );

    await waitFor(() => {
      expect(onDirtyChange).toHaveBeenCalledWith(false);
    });
    expect(screen.getByLabelText('تاريخ الإحالة *')).toHaveValue('');
  });

  it('becomes dirty when assignedDate changes', async () => {
    const user = userEvent.setup();
    render(
      <AssignmentFormPanel
        transactionId={1}
        departments={departments}
        existingDepartmentIds={[]}
        onDirtyChange={onDirtyChange}
        onSuccess={onSuccess}
        onCancel={onCancel}
      />,
    );

    const dateInput = screen.getByLabelText('تاريخ الإحالة *');
    onDirtyChange.mockClear();

    await user.clear(dateInput);
    await user.type(dateInput, '10/06/1447');

    await waitFor(() => expect(onDirtyChange).toHaveBeenCalledWith(true));

    onDirtyChange.mockClear();
    await user.clear(dateInput);

    await waitFor(() => expect(onDirtyChange).toHaveBeenCalledWith(false));
  });

  it('requires assignedDate before saving', async () => {
    const user = userEvent.setup();
    render(
      <AssignmentFormPanel
        transactionId={1}
        departments={departments}
        existingDepartmentIds={[]}
        onDirtyChange={onDirtyChange}
        onSuccess={onSuccess}
        onCancel={onCancel}
      />,
    );

    await user.selectOptions(screen.getByLabelText('الإدارة *'), '1');
    await user.click(screen.getByRole('button', { name: 'حفظ الاحالة' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('تاريخ الإحالة مطلوب.');
    expect(services.transactionsApi.addAssignment).not.toHaveBeenCalled();
  });

  it('rejects future assignedDate before saving', async () => {
    const user = userEvent.setup();
    render(
      <AssignmentFormPanel
        transactionId={1}
        departments={departments}
        existingDepartmentIds={[]}
        onDirtyChange={onDirtyChange}
        onSuccess={onSuccess}
        onCancel={onCancel}
      />,
    );

    await user.selectOptions(screen.getByLabelText('الإدارة *'), '1');
    fireEvent.change(screen.getByLabelText('تاريخ الإحالة - اختيار من التقويم'), {
      target: { value: addDaysIso(todayLocalIso(), 1) },
    });
    await user.click(screen.getByRole('button', { name: 'حفظ الاحالة' }));

    expect(await screen.findByRole('alert')).toHaveTextContent('لا يمكن أن يكون التاريخ بعد تاريخ اليوم.');
    expect(services.transactionsApi.addAssignment).not.toHaveBeenCalled();
  });

  it('always starts with an empty letter number, never inheriting a prior referral number', () => {
    render(
      <AssignmentFormPanel
        transactionId={1}
        departments={departments}
        existingDepartmentIds={[]}
        onDirtyChange={onDirtyChange}
        onSuccess={onSuccess}
        onCancel={onCancel}
      />,
    );

    expect(screen.getByLabelText('رقم الخطاب')).toHaveValue('');
  });

  it('does not submit a letter number the user never entered', async () => {
    const user = userEvent.setup();
    render(
      <AssignmentFormPanel
        transactionId={1}
        departments={departments}
        existingDepartmentIds={[]}
        onDirtyChange={onDirtyChange}
        onSuccess={onSuccess}
        onCancel={onCancel}
      />,
    );

    await user.selectOptions(screen.getByLabelText('الإدارة *'), '1');
    await user.type(screen.getByLabelText('تاريخ الإحالة *'), '10/06/1447');
    await user.click(screen.getByRole('button', { name: 'حفظ الاحالة' }));

    await waitFor(() => expect(services.transactionsApi.addAssignment).toHaveBeenCalled());
    const payload = vi.mocked(services.transactionsApi.addAssignment).mock.calls[0][1];
    expect(payload.letterNumber).toBeNull();
  });
});
