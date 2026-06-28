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
  });

  afterEach(() => {
    cleanup();
  });

  it('opens a modal and links using the internal followUp.id from the transaction follow-up list', async () => {
    const user = userEvent.setup();
    vi.mocked(services.transactionsApi.getFollowUps).mockResolvedValue({
      data: [
        {
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
        },
      ],
    } as never);

    render(
      <MemoryRouter>
        <FollowUpPrintPendingPage />
      </MemoryRouter>,
    );

    await waitFor(() => expect(screen.getByText('IN-77')).toBeInTheDocument());

    await user.click(screen.getByRole('button', { name: 'ربط تعقيب' }));

    const dialog = await screen.findByRole('dialog', { name: 'ربط تعقيب' });
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

  it('shows an empty-state message when no follow-ups exist for the transaction', async () => {
    const user = userEvent.setup();
    vi.mocked(services.transactionsApi.getFollowUps).mockResolvedValue({ data: [] } as never);

    render(
      <MemoryRouter>
        <FollowUpPrintPendingPage />
      </MemoryRouter>,
    );

    await waitFor(() => expect(screen.getByText('IN-77')).toBeInTheDocument());

    await user.click(screen.getByRole('button', { name: 'ربط تعقيب' }));

    const dialog = await screen.findByRole('dialog', { name: 'ربط تعقيب' });
    expect(within(dialog).getByText(
      'لا توجد تعقيبات مسجلة لهذه المعاملة. سجّل تعقيبًا أولًا من صفحة المعاملة ثم ارجع للربط.',
    )).toBeInTheDocument();
    expect(within(dialog).queryByRole('button', { name: 'ربط هذا التعقيب' })).not.toBeInTheDocument();
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
