import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import AttachmentFormPanel from './AttachmentFormPanel';
import * as services from '../../api/services';

vi.mock('../../api/services', () => ({
  transactionsApi: {
    uploadAttachment: vi.fn(),
  },
}));

vi.mock('../../features/scanner/ScanAttachmentButton', () => ({
  default: () => null,
}));

describe('AttachmentFormPanel', () => {
  const onDirtyChange = vi.fn();
  const onSuccess = vi.fn();
  const onCancel = vi.fn();

  beforeEach(() => {
    vi.clearAllMocks();
  });

  afterEach(() => {
    cleanup();
  });

  function renderPanel() {
    return render(
      <AttachmentFormPanel
        transactionId={1}
        onDirtyChange={onDirtyChange}
        onSuccess={onSuccess}
        onCancel={onCancel}
      />,
    );
  }

  it('shows upload progress in output element', async () => {
    const user = userEvent.setup();
    let resolveUpload: (() => void) | undefined;
    vi.mocked(services.transactionsApi.uploadAttachment).mockImplementation(
      () => new Promise((resolve) => { resolveUpload = () => resolve({ data: { id: 1 } } as never); }),
    );

    renderPanel();
    const input = document.querySelector('input[type="file"]') as HTMLInputElement;
    const file = new File(['data'], 'doc.pdf', { type: 'application/pdf' });
    await user.upload(input, file);

    expect(document.querySelector('output')).toHaveTextContent('جاري رفع doc.pdf');

    resolveUpload?.();
    await waitFor(() => expect(onSuccess).toHaveBeenCalled());
  });

  it('resets dirty state after failed upload', async () => {
    const user = userEvent.setup();
    vi.mocked(services.transactionsApi.uploadAttachment).mockRejectedValue(new Error('fail'));

    renderPanel();
    const input = document.querySelector('input[type="file"]') as HTMLInputElement;
    await user.upload(input, new File(['data'], 'doc.pdf', { type: 'application/pdf' }));

    await waitFor(() => {
      expect(onDirtyChange).toHaveBeenCalledWith(false);
      expect(screen.getByText(/تعذر|fail/i)).toBeInTheDocument();
    });
  });

  it('prevents duplicate uploads while in progress', async () => {
    const user = userEvent.setup();
    vi.mocked(services.transactionsApi.uploadAttachment).mockImplementation(
      () => new Promise(() => {}),
    );

    renderPanel();
    const input = document.querySelector('input[type="file"]') as HTMLInputElement;
    const file = new File(['data'], 'doc.pdf', { type: 'application/pdf' });
    await user.upload(input, file);
    await user.upload(input, file);

    expect(services.transactionsApi.uploadAttachment).toHaveBeenCalledTimes(1);
  });

  it('resets file input after selection', async () => {
    const user = userEvent.setup();
    vi.mocked(services.transactionsApi.uploadAttachment).mockResolvedValue({ data: { id: 1 } } as never);

    renderPanel();
    const input = document.querySelector('input[type="file"]') as HTMLInputElement;
    await user.upload(input, new File(['data'], 'doc.pdf', { type: 'application/pdf' }));

    await waitFor(() => expect(onSuccess).toHaveBeenCalled());
    expect(input.value).toBe('');
  });
});
