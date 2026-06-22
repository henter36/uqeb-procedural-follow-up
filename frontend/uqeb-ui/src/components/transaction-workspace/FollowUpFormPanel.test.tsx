import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import FollowUpFormPanel from './FollowUpFormPanel';
import * as services from '../../api/services';

vi.mock('../../api/services', () => ({
  transactionsApi: {
    getFollowUpDepartments: vi.fn(),
    addFollowUp: vi.fn(),
  },
}));

const departments = [
  { departmentId: 1, departmentName: 'إدارة أ', isDefaultSelected: true },
  { departmentId: 2, departmentName: 'إدارة ب', isDefaultSelected: false },
];

describe('FollowUpFormPanel', () => {
  const onDirtyChange = vi.fn();
  const onSuccess = vi.fn();
  const onCancel = vi.fn();

  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(services.transactionsApi.getFollowUpDepartments).mockResolvedValue({ data: departments } as never);
  });

  afterEach(() => {
    cleanup();
  });

  it('loads departments and selects defaults', async () => {
    render(
      <FollowUpFormPanel
        transactionId={1}
        onDirtyChange={onDirtyChange}
        onSuccess={onSuccess}
        onCancel={onCancel}
      />,
    );

    await waitFor(() => {
      expect(screen.getByLabelText('رقم التعقيب')).toBeInTheDocument();
    });
    expect(services.transactionsApi.getFollowUpDepartments).toHaveBeenCalledWith(1);
  });

  it('shows cancel while loading', () => {
    vi.mocked(services.transactionsApi.getFollowUpDepartments).mockImplementation(
      () => new Promise(() => {}),
    );

    render(
      <FollowUpFormPanel
        transactionId={1}
        onDirtyChange={onDirtyChange}
        onSuccess={onSuccess}
        onCancel={onCancel}
      />,
    );

    expect(screen.getByRole('button', { name: 'إلغاء' })).toBeInTheDocument();
  });

  it('shows cancel when no departments exist', async () => {
    vi.mocked(services.transactionsApi.getFollowUpDepartments).mockResolvedValue({ data: [] } as never);

    render(
      <FollowUpFormPanel
        transactionId={1}
        onDirtyChange={onDirtyChange}
        onSuccess={onSuccess}
        onCancel={onCancel}
      />,
    );

    await waitFor(() => {
      expect(screen.getByText(/لا توجد إدارات/)).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'إلغاء' })).toBeInTheDocument();
    });
  });

  it('shows load error and cancel button', async () => {
    vi.mocked(services.transactionsApi.getFollowUpDepartments).mockRejectedValue(new Error('fail'));

    render(
      <FollowUpFormPanel
        transactionId={1}
        onDirtyChange={onDirtyChange}
        onSuccess={onSuccess}
        onCancel={onCancel}
      />,
    );

    await waitFor(() => {
      expect(screen.getByText('تعذر تحميل الإدارات المتاحة')).toBeInTheDocument();
      expect(screen.getByRole('button', { name: 'إلغاء' })).toBeInTheDocument();
    });
  });

  it('prevents save without selected departments', async () => {
    vi.mocked(services.transactionsApi.getFollowUpDepartments).mockResolvedValue({
      data: [{ departmentId: 3, departmentName: 'إدارة ج', isDefaultSelected: false }],
    } as never);

    const user = userEvent.setup();
    render(
      <FollowUpFormPanel
        transactionId={1}
        onDirtyChange={onDirtyChange}
        onSuccess={onSuccess}
        onCancel={onCancel}
      />,
    );

    await waitFor(() => expect(screen.getByRole('button', { name: 'حفظ التعقيب' })).toBeInTheDocument());
    await user.click(screen.getByRole('button', { name: 'حفظ التعقيب' }));

    expect(screen.getByText('يجب اختيار إدارة واحدة على الأقل لإرسال التعقيب.')).toBeInTheDocument();
    expect(services.transactionsApi.addFollowUp).not.toHaveBeenCalled();
  });

  it('does not mark dirty after initial department load', async () => {
    render(
      <FollowUpFormPanel
        transactionId={1}
        onDirtyChange={onDirtyChange}
        onSuccess={onSuccess}
        onCancel={onCancel}
      />,
    );

    await waitFor(() => {
      expect(screen.getByLabelText('رقم التعقيب')).toBeInTheDocument();
    });
    expect(onDirtyChange).toHaveBeenCalledWith(false);
  });

  it('becomes dirty when followUpDate changes only', async () => {
    const user = userEvent.setup();
    render(
      <FollowUpFormPanel
        transactionId={1}
        onDirtyChange={onDirtyChange}
        onSuccess={onSuccess}
        onCancel={onCancel}
      />,
    );

    await waitFor(() => expect(screen.getByLabelText('تاريخ التعقيب')).toBeInTheDocument());
    const dateInput = screen.getByLabelText('تاريخ التعقيب');
    const originalDate = (dateInput as HTMLInputElement).value;
    onDirtyChange.mockClear();

    await user.clear(dateInput);
    await user.type(dateInput, '2025-11-15');

    await waitFor(() => expect(onDirtyChange).toHaveBeenCalledWith(true));

    onDirtyChange.mockClear();
    await user.clear(dateInput);
    await user.type(dateInput, originalDate);

    await waitFor(() => expect(onDirtyChange).toHaveBeenCalledWith(false));
  });

  it('becomes dirty when departments change only', async () => {
    const user = userEvent.setup();
    render(
      <FollowUpFormPanel
        transactionId={1}
        onDirtyChange={onDirtyChange}
        onSuccess={onSuccess}
        onCancel={onCancel}
      />,
    );

    await waitFor(() => expect(screen.getByLabelText('إدارة ب')).toBeInTheDocument());
    onDirtyChange.mockClear();

    await user.click(screen.getByLabelText('إدارة ب'));

    await waitFor(() => expect(onDirtyChange).toHaveBeenCalledWith(true));

    onDirtyChange.mockClear();
    await user.click(screen.getByLabelText('إدارة ب'));

    await waitFor(() => expect(onDirtyChange).toHaveBeenCalledWith(false));
  });
});
