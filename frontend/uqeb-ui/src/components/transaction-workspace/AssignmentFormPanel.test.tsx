import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import AssignmentFormPanel from './AssignmentFormPanel';
import * as services from '../../api/services';
import { formatHijriInputParts, gregorianToHijriParts } from '../../utils/hijriDateInput';
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
    const todayHijri = gregorianToHijriParts(todayLocalIso());
    expect(todayHijri).not.toBeNull();
    expect(screen.getByLabelText(/تاريخ الاحالة/)).toHaveValue(formatHijriInputParts(todayHijri!));
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

    const dateInput = screen.getByLabelText(/تاريخ الاحالة/);
    const originalDate = (dateInput as HTMLInputElement).value;
    onDirtyChange.mockClear();

    await user.clear(dateInput);
    await user.type(dateInput, '1447/06/10');

    await waitFor(() => expect(onDirtyChange).toHaveBeenCalledWith(true));

    onDirtyChange.mockClear();
    await user.clear(dateInput);
    await user.type(dateInput, originalDate);

    await waitFor(() => expect(onDirtyChange).toHaveBeenCalledWith(false));
  });
});
