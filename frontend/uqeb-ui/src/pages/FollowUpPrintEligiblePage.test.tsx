import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter } from 'react-router-dom';
import FollowUpPrintEligiblePage from './FollowUpPrintEligiblePage';
import * as services from '../api/services';

vi.mock('../utils/createIdempotencyKey', () => ({
  createIdempotencyKey: vi.fn(),
}));

vi.mock('../api/services', () => ({
  categoriesApi: { getAll: vi.fn() },
  departmentsApi: { getAll: vi.fn() },
  followUpPrintApi: {
    getEligible: vi.fn(),
    previewJob: vi.fn(),
    createJob: vi.fn(),
  },
  letterTemplatesApi: { list: vi.fn() },
}));

const { createIdempotencyKey } = await import('../utils/createIdempotencyKey');

function mockReferenceData() {
  vi.mocked(services.letterTemplatesApi.list).mockResolvedValue({
    data: [
      {
        id: 10,
        name: 'قالب 1',
        type: 'FollowUp',
        content: '',
        isActive: true,
        isDefault: true,
        createdAt: '2026-01-01T00:00:00Z',
        updatedAt: '2026-01-01T00:00:00Z',
      },
      {
        id: 11,
        name: 'قالب 2',
        type: 'FollowUp',
        content: '',
        isActive: true,
        isDefault: false,
        createdAt: '2026-01-01T00:00:00Z',
        updatedAt: '2026-01-01T00:00:00Z',
      },
    ],
  } as never);
  vi.mocked(services.departmentsApi.getAll).mockResolvedValue({ data: [] } as never);
  vi.mocked(services.categoriesApi.getAll).mockResolvedValue({ data: [] } as never);
}

function mockEligibleData() {
  vi.mocked(services.followUpPrintApi.getEligible).mockResolvedValue({
    data: {
      totalCount: 1,
      page: 1,
      pageSize: 25,
      items: [
        {
          transactionId: 1,
          incomingNumber: 'IN-1',
          subject: 'معاملة',
          incomingDate: '2026-01-01T00:00:00Z',
          referenceDate: '2026-01-01T00:00:00Z',
          daysSinceReference: 20,
          expectedFollowUpSequence: 1,
          recentlyPrintedExcluded: false,
          primaryTargetEntity: 'إدارة',
        },
      ],
    },
  } as never);
  vi.mocked(services.followUpPrintApi.previewJob).mockResolvedValue({
    data: {
      matchedCount: 1,
      eligibleTransactionCount: 1,
      recentlyPrintedExcludedCount: 0,
      notDueYetCount: 0,
      noTargetCount: 0,
      estimatedLetterCount: 1,
      estimatedPartCount: 1,
    },
  } as never);
}

describe('FollowUpPrintEligiblePage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    let key = 0;
    vi.mocked(createIdempotencyKey).mockImplementation(() => `key-${++key}`);
    mockReferenceData();
    mockEligibleData();
    vi.mocked(services.followUpPrintApi.createJob).mockRejectedValue({
      response: { status: 500 },
    });
  });

  afterEach(() => {
    cleanup();
  });

  async function renderReady() {
    render(
      <MemoryRouter>
        <FollowUpPrintEligiblePage />
      </MemoryRouter>,
    );
    await waitFor(() => expect(screen.getByText('IN-1')).toBeInTheDocument());
  }

  it('reuses the same idempotencyKey when retrying the same create attempt', async () => {
    const user = userEvent.setup();
    await renderReady();

    const createButton = screen.getByRole('button', { name: 'إنشاء مهمة طباعة' });
    await user.click(createButton);
    await waitFor(() => expect(services.followUpPrintApi.createJob).toHaveBeenCalledTimes(1));
    await screen.findByText('تعذر تأكيد إنشاء المهمة. تحقق من قائمة المهام قبل إعادة المحاولة.');

    await user.click(createButton);
    await waitFor(() => expect(services.followUpPrintApi.createJob).toHaveBeenCalledTimes(2));

    const first = vi.mocked(services.followUpPrintApi.createJob).mock.calls[0][0].idempotencyKey;
    const second = vi.mocked(services.followUpPrintApi.createJob).mock.calls[1][0].idempotencyKey;
    expect(second).toBe(first);
  });

  it('resets idempotencyKey when the template changes', async () => {
    const user = userEvent.setup();
    await renderReady();

    await user.click(screen.getByRole('button', { name: 'إنشاء مهمة طباعة' }));
    await waitFor(() => expect(services.followUpPrintApi.createJob).toHaveBeenCalledTimes(1));
    const first = vi.mocked(services.followUpPrintApi.createJob).mock.calls[0][0].idempotencyKey;

    await user.selectOptions(screen.getByLabelText('قالب الخطاب'), '11');
    await user.click(screen.getByRole('button', { name: 'إنشاء مهمة طباعة' }));
    await waitFor(() => expect(services.followUpPrintApi.createJob).toHaveBeenCalledTimes(2));

    const second = vi.mocked(services.followUpPrintApi.createJob).mock.calls[1][0].idempotencyKey;
    expect(second).not.toBe(first);
  });

  it('disables the create button while creating', async () => {
    const user = userEvent.setup();
    let resolveCreate: (value: unknown) => void = () => undefined;
    vi.mocked(services.followUpPrintApi.createJob).mockReturnValue(
      new Promise((resolve) => { resolveCreate = resolve; }) as never,
    );
    await renderReady();

    const createButton = screen.getByRole('button', { name: 'إنشاء مهمة طباعة' });
    await user.click(createButton);

    expect(screen.getByRole('button', { name: 'جاري الإنشاء...' })).toBeDisabled();
    resolveCreate({ data: { id: 99 } });
  });
});
