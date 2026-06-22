import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import FollowUpLetterFormPanel from './FollowUpLetterFormPanel';
import * as services from '../../api/services';

vi.mock('../../api/services', () => ({
  transactionsApi: {
    previewFollowUpLetter: vi.fn(),
    downloadFollowUpLetterPdf: vi.fn(),
  },
}));

vi.mock('../../utils/downloadBlob', () => ({
  downloadBlob: vi.fn(),
}));

import { downloadBlob } from '../../utils/downloadBlob';

const baseTx = {
  id: 1,
  outgoingDepartments: [{ departmentId: 1, departmentName: 'إدارة أ' }],
  incomingFrom: 'جهة',
} as never;

describe('FollowUpLetterFormPanel', () => {
  const onDirtyChange = vi.fn();
  const onDownloaded = vi.fn();
  const onCancel = vi.fn();

  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(services.transactionsApi.previewFollowUpLetter).mockResolvedValue({
      data: { content: 'نص الخطاب الأولي', targetEntity: 'إدارة أ' },
    } as never);
  });

  afterEach(() => {
    cleanup();
  });

  it('does not mark dirty after initial template load', async () => {
    render(
      <FollowUpLetterFormPanel
        transactionId={1}
        tx={baseTx}
        assignments={[]}
        onDirtyChange={onDirtyChange}
        onDownloaded={onDownloaded}
        onCancel={onCancel}
      />,
    );

    await waitFor(() => {
      expect(screen.getByDisplayValue('نص الخطاب الأولي')).toBeInTheDocument();
    });
    expect(onDirtyChange).toHaveBeenCalledWith(false);
  });

  it('marks dirty when recipient changes', async () => {
    const user = userEvent.setup();
    render(
      <FollowUpLetterFormPanel
        transactionId={1}
        tx={baseTx}
        assignments={[]}
        onDirtyChange={onDirtyChange}
        onDownloaded={onDownloaded}
        onCancel={onCancel}
      />,
    );

    await waitFor(() => expect(screen.getByLabelText('الجهة')).toBeInTheDocument());
    await user.clear(screen.getByLabelText('الجهة'));
    await user.type(screen.getByLabelText('الجهة'), 'جهة جديدة');

    await waitFor(() => expect(onDirtyChange).toHaveBeenCalledWith(true));
  });

  it('clears dirty after successful download', async () => {
    const user = userEvent.setup();
    vi.mocked(services.transactionsApi.downloadFollowUpLetterPdf).mockResolvedValue({
      data: new Blob(['pdf']),
    } as never);

    render(
      <FollowUpLetterFormPanel
        transactionId={1}
        tx={baseTx}
        assignments={[]}
        onDirtyChange={onDirtyChange}
        onDownloaded={onDownloaded}
        onCancel={onCancel}
      />,
    );

    await waitFor(() => expect(screen.getByRole('button', { name: 'تحميل PDF' })).toBeEnabled());
    await user.click(screen.getByRole('button', { name: 'تحميل PDF' }));

    await waitFor(() => {
      expect(downloadBlob).toHaveBeenCalled();
      expect(onDirtyChange).toHaveBeenCalledWith(false);
      expect(onDownloaded).toHaveBeenCalled();
    });
  });

  it('shows preview error without changing baseline', async () => {
    const user = userEvent.setup();
    vi.mocked(services.transactionsApi.previewFollowUpLetter)
      .mockResolvedValueOnce({ data: { content: 'نص الخطاب الأولي', targetEntity: 'إدارة أ' } } as never)
      .mockRejectedValueOnce(new Error('preview fail'));

    render(
      <FollowUpLetterFormPanel
        transactionId={1}
        tx={baseTx}
        assignments={[]}
        onDirtyChange={onDirtyChange}
        onDownloaded={onDownloaded}
        onCancel={onCancel}
      />,
    );

    await waitFor(() => expect(screen.getByRole('button', { name: 'معاينة' })).toBeInTheDocument());
    await user.click(screen.getByRole('button', { name: 'معاينة' }));

    await waitFor(() => expect(screen.getByText(/preview fail|تعذر/i)).toBeInTheDocument());
  });
});
