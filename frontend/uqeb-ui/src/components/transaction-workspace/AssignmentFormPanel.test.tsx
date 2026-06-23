import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import AssignmentFormPanel from './AssignmentFormPanel';
import * as services from '../../api/services';
import { todayLocalIso } from '../../utils/localDate';

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
    expect(screen.getByLabelText('تاريخ التحويل')).toHaveValue(todayLocalIso());
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

    const dateInput = screen.getByLabelText('تاريخ التحويل');
    const originalDate = (dateInput as HTMLInputElement).value;
    onDirtyChange.mockClear();

    await user.clear(dateInput);
    await user.type(dateInput, '2025-12-01');

    await waitFor(() => expect(onDirtyChange).toHaveBeenCalledWith(true));

    onDirtyChange.mockClear();
    await user.clear(dateInput);
    await user.type(dateInput, originalDate);

    await waitFor(() => expect(onDirtyChange).toHaveBeenCalledWith(false));
  });
});
