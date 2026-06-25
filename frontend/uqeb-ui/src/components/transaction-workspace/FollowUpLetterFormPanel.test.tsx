import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import FollowUpLetterFormPanel from './FollowUpLetterFormPanel';
import * as services from '../../api/services';

vi.mock('../../api/services', () => ({
  transactionsApi: {
    previewFollowUpLetter: vi.fn(),
    downloadFollowUpLetterPdf: vi.fn(),
  },
  followUpPrintApi: {
    getTransactionPrintView: vi.fn(),
  },
}));

vi.mock('../../utils/downloadBlob', () => ({
  downloadBlob: vi.fn(),
}));

vi.mock('../../utils/followUpPrintWindow', () => ({
  openHtmlPrintWindow: vi.fn(),
}));

import { downloadBlob } from '../../utils/downloadBlob';
import { openHtmlPrintWindow } from '../../utils/followUpPrintWindow';

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

  it('opens read-only preview modal without calling preview API again', async () => {
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

    await waitFor(() => expect(screen.getByDisplayValue('نص الخطاب الأولي')).toBeInTheDocument());
    expect(services.transactionsApi.previewFollowUpLetter).toHaveBeenCalledTimes(1);

    await user.type(screen.getByLabelText('نص الخطاب'), ' تعديل محلي');
    await user.click(screen.getByRole('button', { name: 'معاينة' }));

    expect(services.transactionsApi.previewFollowUpLetter).toHaveBeenCalledTimes(1);
    const dialog = screen.getByRole('dialog', { name: 'معاينة الخطاب' });
    expect(within(dialog).getByText(/نص الخطاب الأولي تعديل محلي/)).toBeInTheDocument();
    expect(within(dialog).getByText('إدارة أ')).toBeInTheDocument();
  });

  it('regenerates from template via API', async () => {
    const user = userEvent.setup();
    vi.mocked(services.transactionsApi.previewFollowUpLetter)
      .mockResolvedValueOnce({ data: { content: 'نص الخطاب الأولي', targetEntity: 'إدارة أ' } } as never)
      .mockResolvedValueOnce({ data: { content: 'نص مجدد', targetEntity: 'إدارة ب' } } as never);

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

    await waitFor(() => expect(screen.getByRole('button', { name: 'إعادة توليد النص من القالب' })).toBeEnabled());
    await user.click(screen.getByRole('button', { name: 'إعادة توليد النص من القالب' }));

    await waitFor(() => {
      expect(screen.getByDisplayValue('نص مجدد')).toBeInTheDocument();
      expect(screen.getByDisplayValue('إدارة ب')).toBeInTheDocument();
    });
    expect(services.transactionsApi.previewFollowUpLetter).toHaveBeenCalledTimes(2);
  });

  it('opens direct print view via API', async () => {
    const user = userEvent.setup();
    vi.mocked(services.followUpPrintApi.getTransactionPrintView).mockResolvedValue({
      data: '<html>print</html>',
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

    await waitFor(() => expect(screen.getByRole('button', { name: 'طباعة مباشرة' })).toBeEnabled());
    await user.click(screen.getByRole('button', { name: 'طباعة مباشرة' }));

    await waitFor(() => {
      expect(services.followUpPrintApi.getTransactionPrintView).toHaveBeenCalledWith(1, {
        targetEntity: 'إدارة أ',
        content: 'نص الخطاب الأولي',
      });
      expect(openHtmlPrintWindow).toHaveBeenCalledWith('<html>print</html>');
    });
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

  it('keeps dirty false after download following edits and rerender', async () => {
    const user = userEvent.setup();
    vi.mocked(services.transactionsApi.downloadFollowUpLetterPdf).mockResolvedValue({
      data: new Blob(['pdf']),
    } as never);

    const view = render(
      <FollowUpLetterFormPanel
        transactionId={1}
        tx={baseTx}
        assignments={[]}
        onDirtyChange={onDirtyChange}
        onDownloaded={onDownloaded}
        onCancel={onCancel}
      />,
    );

    await waitFor(() => expect(screen.getByLabelText('نص الخطاب')).toBeInTheDocument());
    await user.type(screen.getByLabelText('نص الخطاب'), ' تعديل');
    await waitFor(() => expect(onDirtyChange).toHaveBeenCalledWith(true));

    onDirtyChange.mockClear();
    await user.click(screen.getByRole('button', { name: 'تحميل PDF' }));

    await waitFor(() => expect(onDirtyChange).toHaveBeenLastCalledWith(false));

    onDirtyChange.mockClear();
    view.rerender(
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
      expect(onDirtyChange.mock.calls.filter(([dirty]) => dirty)).toHaveLength(0);
    });
  });
});
