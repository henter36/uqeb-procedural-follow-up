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

async function openDepartmentsDropdown(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole('button', {
    name: /لم يتم اختيار أي إدارة|إدارة واحدة مختارة|إدارتان مختارتان/,
  }));
}

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

  it('calls onSuccess with the FollowUp returned by addFollowUp', async () => {
    const fakeFollowUp = {
      id: 42,
      followUpDate: '2026-01-15T00:00:00Z',
      followUpNumber: 'F-042',
      recipients: [],
      departments: [],
      requiresReply: false,
      replyStatus: 'None',
      createdByName: 'مختبر',
      createdAt: '2026-01-15T00:00:00Z',
    };
    vi.mocked(services.transactionsApi.addFollowUp).mockResolvedValue({ data: fakeFollowUp } as never);

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
    await user.type(screen.getByLabelText('تاريخ التعقيب *'), '16/01/1448');
    await user.click(screen.getByRole('button', { name: 'حفظ التعقيب' }));

    await waitFor(() => {
      expect(services.transactionsApi.addFollowUp).toHaveBeenCalledWith(1, expect.any(Object));
      expect(onSuccess).toHaveBeenCalledWith(fakeFollowUp);
    });
  });

  it('opens with an empty follow-up date and does not default to today', async () => {
    render(
      <FollowUpFormPanel
        transactionId={1}
        onDirtyChange={onDirtyChange}
        onSuccess={onSuccess}
        onCancel={onCancel}
      />,
    );

    await waitFor(() => expect(screen.getByLabelText('تاريخ التعقيب *')).toHaveValue(''));
  });

  it('does not submit without a follow-up date', async () => {
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

    expect(await screen.findByText('تاريخ التعقيب مطلوب.')).toBeInTheDocument();
    expect(services.transactionsApi.addFollowUp).not.toHaveBeenCalled();
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

    await waitFor(() => expect(screen.getByLabelText('تاريخ التعقيب *')).toHaveValue(''));
    const dateInput = screen.getByLabelText('تاريخ التعقيب *');
    onDirtyChange.mockClear();

    await user.type(dateInput, '24/05/1447');

    await waitFor(() => expect(onDirtyChange).toHaveBeenCalledWith(true));

    onDirtyChange.mockClear();
    await user.clear(dateInput);

    await waitFor(() => expect(onDirtyChange).toHaveBeenCalledWith(false));
  });

  it('converts Hijri follow-up date to Gregorian ISO before submit', async () => {
    vi.mocked(services.transactionsApi.addFollowUp).mockResolvedValue({
      data: {
        id: 44,
        followUpDate: '2026-07-01T00:00:00Z',
        recipients: [],
        departments: [],
      },
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

    await waitFor(() => expect(screen.getByLabelText('تاريخ التعقيب *')).toBeInTheDocument());
    const dateInput = screen.getByLabelText('تاريخ التعقيب *');
    await user.clear(dateInput);
    await user.type(dateInput, '16/01/1448');
    await user.click(screen.getByRole('button', { name: 'حفظ التعقيب' }));

    await waitFor(() => {
      expect(services.transactionsApi.addFollowUp).toHaveBeenCalledWith(1, expect.objectContaining({
        followUpDate: '2026-07-01T00:00:00',
      }));
    });
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

    await waitFor(() => expect(screen.getByRole('button', { name: /إدارة واحدة مختارة/ })).toBeInTheDocument());
    await openDepartmentsDropdown(user);
    onDirtyChange.mockClear();

    await user.click(screen.getByLabelText('إدارة ب'));

    await waitFor(() => expect(onDirtyChange).toHaveBeenCalledWith(true));

    onDirtyChange.mockClear();
    await user.click(screen.getByLabelText('إدارة ب'));

    await waitFor(() => expect(onDirtyChange).toHaveBeenCalledWith(false));
  });

  it('clears stale departments when transactionId changes before the next load completes', async () => {
    const user = userEvent.setup();
    const departmentsTx2 = [
      { departmentId: 9, departmentName: 'إدارة معاملة 2', isDefaultSelected: true },
    ];
    let resolveTx2: ((value: never) => void) | undefined;

    vi.mocked(services.transactionsApi.getFollowUpDepartments)
      .mockResolvedValueOnce({ data: departments } as never)
      .mockImplementationOnce(
        () => new Promise((resolve) => { resolveTx2 = resolve; }),
      );

    const props = {
      onDirtyChange,
      onSuccess,
      onCancel,
    };

    const { rerender } = render(<FollowUpFormPanel transactionId={1} {...props} />);

    await waitFor(() => expect(screen.getByRole('button', { name: /إدارة واحدة مختارة/ })).toBeInTheDocument());

    rerender(<FollowUpFormPanel transactionId={2} {...props} />);

    expect(screen.queryByLabelText('إدارة أ')).not.toBeInTheDocument();
    expect(screen.getByText(/جاري تحميل الإدارات/)).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'حفظ التعقيب' })).not.toBeInTheDocument();
    expect(onDirtyChange).toHaveBeenCalledWith(false);

    resolveTx2!({ data: departmentsTx2 } as never);
    await waitFor(() => expect(screen.getByRole('button', { name: /إدارة واحدة مختارة/ })).toBeInTheDocument());
    await openDepartmentsDropdown(user);
    expect(screen.getByLabelText('إدارة معاملة 2')).toBeInTheDocument();
    expect(services.transactionsApi.getFollowUpDepartments).toHaveBeenLastCalledWith(2);
  });
});
