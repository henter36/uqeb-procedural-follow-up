import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import FollowUpPrintPendingPage from './FollowUpPrintPendingPage';
import * as services from '../api/services';

vi.mock('../hooks/usePendingPrintSummary', () => ({
  usePendingPrintSummary: () => ({ refresh: vi.fn() }),
}));

vi.mock('../context/useAuth', () => ({
  useAuth: () => ({ canClose: true }),
}));

vi.mock('../components/transaction-workspace/FollowUpFormPanel', () => ({
  default: ({
    onSuccess,
    onCancel,
  }: {
    onSuccess: (followUp: unknown) => void;
    onCancel: () => void;
  }) => (
    <>
      <button
        type="button"
        onClick={() =>
          onSuccess({
            id: 101,
            followUpDate: '2026-01-10T00:00:00Z',
            followUpNumber: 'F-NEW',
            recipients: [],
            departments: [],
            requiresReply: false,
            replyStatus: 'None',
            createdByName: 'اختبار',
            createdAt: '2026-01-10T00:00:00Z',
          })
        }
      >
        حفظ التعقيب
      </button>
      <button type="button" onClick={onCancel}>
        إلغاء
      </button>
    </>
  ),
}));

vi.mock('../api/services', () => ({
  followUpPrintApi: {
    getPendingSummary: vi.fn(),
    getPending: vi.fn(),
    confirmRecord: vi.fn(),
    getRecordPrintView: vi.fn(),
    cancelRecord: vi.fn(),
    reprintRecord: vi.fn(),
    linkFollowUp: vi.fn(),
  },
  transactionsApi: {
    getFollowUps: vi.fn(),
    addFollowUp: vi.fn(),
  },
}));

const baseRecord = {
  id: 7,
  transactionId: 77,
  incomingNumber: 'IN-77',
  subject: 'خطاب طباعة',
  targetEntityNameSnapshot: 'جهة مستهدفة',
  followUpSequence: 1,
  printRequestedAt: '2026-01-01T00:00:00Z',
  printConfirmedAt: null,
};

const sampleFollowUp = {
  id: 101,
  followUpNumber: 'F-123',
  followUpDate: '2026-01-05T00:00:00Z',
  sentTo: 'جهة مرسلة',
  recipients: [],
  departments: [{ departmentId: 5, departmentName: 'إدارة الاختبار' }],
  notes: 'مختصر التعقيب',
  requiresReply: true,
  replyStatus: 'Pending',
  createdByName: 'مختبر',
  createdAt: '2026-01-05T00:00:00Z',
};

describe('FollowUpPrintPendingPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(services.followUpPrintApi.getPendingSummary).mockResolvedValue({
      data: { total: 1, withinExclusionDays: 1, olderThanExclusionDays: 0 },
    } as never);
    vi.mocked(services.followUpPrintApi.getPending).mockResolvedValue({
      data: [baseRecord],
    } as never);
    vi.mocked(services.followUpPrintApi.confirmRecord).mockResolvedValue({ data: baseRecord } as never);
    vi.mocked(services.followUpPrintApi.getRecordPrintView).mockResolvedValue({
      data: { html: '<html><body>letter</body></html>', warning: null },
    } as never);
    vi.mocked(services.followUpPrintApi.cancelRecord).mockResolvedValue({ data: baseRecord } as never);
    vi.mocked(services.followUpPrintApi.reprintRecord).mockResolvedValue({ data: baseRecord } as never);
    vi.mocked(services.followUpPrintApi.linkFollowUp).mockResolvedValue({ data: {} } as never);
  });

  afterEach(() => {
    cleanup();
  });

  it('registers a new follow-up and links it automatically via the primary action', async () => {
    const user = userEvent.setup();

    render(
      <MemoryRouter>
        <FollowUpPrintPendingPage />
      </MemoryRouter>,
    );

    await waitFor(() => expect(screen.getByText('IN-77')).toBeInTheDocument());

    await user.click(screen.getByRole('button', { name: 'تسجيل التعقيب' }));

    const dialog = await screen.findByRole('dialog', { name: 'تسجيل التعقيب' });
    expect(dialog).toBeInTheDocument();

    await user.click(within(dialog).getByRole('button', { name: 'حفظ التعقيب' }));

    await waitFor(() => {
      expect(services.followUpPrintApi.linkFollowUp).toHaveBeenCalledWith(7, 101);
    });
    expect(screen.getByText('تم تسجيل التعقيب وربطه بسجل الخطاب.')).toBeInTheDocument();
    expect(screen.queryByRole('dialog', { name: 'تسجيل التعقيب' })).not.toBeInTheDocument();
    expect(services.followUpPrintApi.getPending).toHaveBeenCalledTimes(2);
  });

  it('opens the link-existing modal and links using the internal followUp.id', async () => {
    const user = userEvent.setup();
    vi.mocked(services.transactionsApi.getFollowUps).mockResolvedValue({
      data: [sampleFollowUp],
    } as never);

    render(
      <MemoryRouter>
        <FollowUpPrintPendingPage />
      </MemoryRouter>,
    );

    await waitFor(() => expect(screen.getByText('IN-77')).toBeInTheDocument());

    await user.click(screen.getByRole('button', { name: 'ربط تعقيب موجود' }));

    const dialog = await screen.findByRole('dialog', { name: 'ربط تعقيب موجود' });
    expect(within(dialog).getByText('F-123 · #101')).toBeInTheDocument();
    expect(within(dialog).getByText(/التاريخ:/)).toBeInTheDocument();
    expect(within(dialog).getByText(/الجهة:/)).toBeInTheDocument();
    expect(within(dialog).getByText(/مختصر:/)).toBeInTheDocument();

    await user.click(within(dialog).getByRole('button', { name: 'ربط هذا التعقيب' }));

    await waitFor(() => {
      expect(services.followUpPrintApi.linkFollowUp).toHaveBeenCalledWith(7, 101);
    });
    expect(services.followUpPrintApi.linkFollowUp).not.toHaveBeenCalledWith(7, 'F-123');
  });

  it('shows an empty-state message inside the link-existing modal when no follow-ups exist', async () => {
    const user = userEvent.setup();
    vi.mocked(services.transactionsApi.getFollowUps).mockResolvedValue({ data: [] } as never);

    render(
      <MemoryRouter>
        <FollowUpPrintPendingPage />
      </MemoryRouter>,
    );

    await waitFor(() => expect(screen.getByText('IN-77')).toBeInTheDocument());

    await user.click(screen.getByRole('button', { name: 'ربط تعقيب موجود' }));

    const dialog = await screen.findByRole('dialog', { name: 'ربط تعقيب موجود' });
    expect(within(dialog).getByText(
      'لا توجد تعقيبات مسجلة لهذه المعاملة. استخدم "تسجيل التعقيب" لتسجيل تعقيب جديد وربطه مباشرة.',
    )).toBeInTheDocument();
    expect(within(dialog).queryByRole('button', { name: 'ربط هذا التعقيب' })).not.toBeInTheDocument();
  });

  it('shows the link error inside the modal without closing it', async () => {
    const user = userEvent.setup();
    vi.mocked(services.transactionsApi.getFollowUps).mockResolvedValue({
      data: [sampleFollowUp],
    } as never);
    vi.mocked(services.followUpPrintApi.linkFollowUp).mockRejectedValue(new Error('فشل الربط'));

    render(
      <MemoryRouter>
        <FollowUpPrintPendingPage />
      </MemoryRouter>,
    );

    await waitFor(() => expect(screen.getByText('IN-77')).toBeInTheDocument());

    await user.click(screen.getByRole('button', { name: 'ربط تعقيب موجود' }));

    const dialog = await screen.findByRole('dialog', { name: 'ربط تعقيب موجود' });
    await user.click(within(dialog).getByRole('button', { name: 'ربط هذا التعقيب' }));

    await waitFor(() => {
      expect(within(dialog).getByText('فشل الربط')).toBeInTheDocument();
    });
    expect(dialog).toBeInTheDocument();
  });

  it('does not crash when a follow-up has undefined recipients or departments', async () => {
    const user = userEvent.setup();
    vi.mocked(services.transactionsApi.getFollowUps).mockResolvedValue({
      data: [
        {
          ...sampleFollowUp,
          sentTo: undefined,
          recipients: undefined as unknown as never[],
          departments: undefined as unknown as never[],
        },
      ],
    } as never);

    render(
      <MemoryRouter>
        <FollowUpPrintPendingPage />
      </MemoryRouter>,
    );

    await waitFor(() => expect(screen.getByText('IN-77')).toBeInTheDocument());

    await user.click(screen.getByRole('button', { name: 'ربط تعقيب موجود' }));

    const dialog = await screen.findByRole('dialog', { name: 'ربط تعقيب موجود' });
    await waitFor(() => {
      expect(within(dialog).getByText(/F-123/)).toBeInTheDocument();
    });
    expect(dialog).toBeInTheDocument();
  });

  it('opens a cancel modal with a reason field instead of a prompt', async () => {
    const user = userEvent.setup();

    render(
      <MemoryRouter>
        <FollowUpPrintPendingPage />
      </MemoryRouter>,
    );

    await waitFor(() => expect(screen.getByText('IN-77')).toBeInTheDocument());

    await user.click(screen.getByRole('button', { name: 'إلغاء' }));

    const dialog = await screen.findByRole('dialog', { name: 'إلغاء سجل الطباعة' });
    expect(within(dialog).getByLabelText('سبب الإلغاء')).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'تأكيد الإلغاء' })).toBeInTheDocument();
  });
});
