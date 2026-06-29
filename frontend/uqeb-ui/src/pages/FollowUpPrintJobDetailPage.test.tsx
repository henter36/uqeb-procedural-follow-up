import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import FollowUpPrintJobDetailPage from './FollowUpPrintJobDetailPage';
import * as services from '../api/services';
import type { FollowUpPrintJob } from '../api/types';

vi.mock('../api/services', () => ({
  followUpPrintApi: {
    getJob: vi.fn(),
    cancelJob: vi.fn(),
    retryJob: vi.fn(),
  },
}));

const staleMessage = 'يبدو أن تجهيز الخطابات متوقف أو لم يكتمل. يرجى إعادة محاولة إنشاء المهمة أو مراجعة سجل الأخطاء.';

function buildJob(overrides: Partial<FollowUpPrintJob> = {}): FollowUpPrintJob {
  return {
    id: 1,
    status: 'Queued',
    templateId: 1,
    totalTransactions: 1,
    totalLetters: 1,
    processedLetters: 0,
    readyLetters: 0,
    failedLetters: 0,
    skippedLetters: 0,
    totalParts: 0,
    readyParts: 0,
    printedParts: 0,
    currentPart: 0,
    createdAt: '2026-06-29T08:45:00Z',
    parts: [],
    ...overrides,
  };
}

function mockPageOpenedBeforeGrace() {
  vi.mocked(Date.now)
    .mockReturnValueOnce(new Date('2026-06-29T08:59:00Z').getTime())
    .mockReturnValue(new Date('2026-06-29T09:00:00Z').getTime());
}

function mockPageOpenedAfterGrace() {
  vi.mocked(Date.now)
    .mockReturnValueOnce(new Date('2026-06-29T08:57:00Z').getTime())
    .mockReturnValue(new Date('2026-06-29T09:00:00Z').getTime());
}

async function renderJob(job: FollowUpPrintJob) {
  vi.mocked(services.followUpPrintApi.getJob).mockResolvedValue({ data: job } as never);
  render(
    <MemoryRouter initialEntries={['/follow-up-print/jobs/1']}>
      <Routes>
        <Route path="/follow-up-print/jobs/:id" element={<FollowUpPrintJobDetailPage />} />
      </Routes>
    </MemoryRouter>,
  );
  await waitFor(() => expect(screen.getByText('مهمة الطباعة #1')).toBeInTheDocument());
}

describe('FollowUpPrintJobDetailPage stale warning', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.spyOn(Date, 'now').mockReturnValue(new Date('2026-06-29T09:00:00Z').getTime());
  });

  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
  });

  it('does not show stale warning for a recent queued job', async () => {
    mockPageOpenedAfterGrace();
    await renderJob(buildJob({ status: 'Queued', createdAt: '2026-06-29T08:45:00Z' }));

    expect(screen.getByText(/المهمة في طابور الانتظار/)).toBeInTheDocument();
    expect(screen.queryByText(staleMessage)).not.toBeInTheDocument();
  });

  it('does not show stale warning for a recent processing job', async () => {
    mockPageOpenedAfterGrace();
    await renderJob(buildJob({ status: 'Processing', createdAt: '2026-06-29T08:45:00Z' }));

    expect(screen.getByText(/جارٍ تجهيز الخطابات/)).toBeInTheDocument();
    expect(screen.queryByText(staleMessage)).not.toBeInTheDocument();
  });

  it('does not show stale warning for ReadyToPrint', async () => {
    mockPageOpenedAfterGrace();
    await renderJob(buildJob({ status: 'ReadyToPrint', createdAt: '2026-06-29T07:00:00Z' }));

    expect(screen.queryByText(staleMessage)).not.toBeInTheDocument();
  });

  it('does not show stale warning for createdAt without timezone', async () => {
    mockPageOpenedAfterGrace();
    await renderJob(buildJob({ status: 'Queued', createdAt: '2026-06-29T07:00:00' }));

    expect(screen.queryByText(staleMessage)).not.toBeInTheDocument();
  });

  it('does not show stale warning during the first two minutes after opening the page', async () => {
    mockPageOpenedBeforeGrace();
    await renderJob(buildJob({ status: 'Queued', createdAt: '2026-06-29T07:00:00Z' }));

    expect(screen.queryByText(staleMessage)).not.toBeInTheDocument();
  });

  it('shows stale warning when only startedAt exists without generation progress', async () => {
    mockPageOpenedAfterGrace();
    await renderJob(buildJob({
      status: 'Processing',
      createdAt: '2026-06-29T07:00:00Z',
      startedAt: '2026-06-29T07:01:00Z',
    }));

    expect(screen.getByText(staleMessage)).toBeInTheDocument();
  });

  it('does not show stale warning when processedLetters is positive', async () => {
    mockPageOpenedAfterGrace();
    await renderJob(buildJob({
      status: 'Processing',
      createdAt: '2026-06-29T07:00:00Z',
      processedLetters: 1,
    }));

    expect(screen.queryByText(staleMessage)).not.toBeInTheDocument();
  });

  it('does not show stale warning when a part is printable', async () => {
    mockPageOpenedAfterGrace();
    await renderJob(buildJob({
      status: 'Processing',
      createdAt: '2026-06-29T07:00:00Z',
      parts: [
        {
          id: 10,
          jobId: 1,
          partNumber: 1,
          status: 'ReadyToPrint',
          letterCount: 1,
          estimatedPages: 1,
          createdAt: '2026-06-29T07:00:00Z',
          readyAt: '2026-06-29T07:10:00Z',
        },
      ],
    }));

    expect(screen.queryByText(staleMessage)).not.toBeInTheDocument();
  });

  it('shows stale warning for an old claimed job without progress', async () => {
    mockPageOpenedAfterGrace();
    await renderJob(buildJob({ status: 'Claimed', createdAt: '2026-06-29T07:00:00Z' }));

    expect(screen.getByText(staleMessage)).toBeInTheDocument();
  });

  it('shows stale warning for an old queued job without progress', async () => {
    mockPageOpenedAfterGrace();
    await renderJob(buildJob({ status: 'Queued', createdAt: '2026-06-29T07:00:00Z' }));

    expect(screen.getByText(staleMessage)).toBeInTheDocument();
  });
});
