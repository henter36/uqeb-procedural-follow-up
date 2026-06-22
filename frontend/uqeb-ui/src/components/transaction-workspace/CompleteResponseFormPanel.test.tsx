import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import CompleteResponseFormPanel from './CompleteResponseFormPanel';
import * as services from '../../api/services';

vi.mock('../../api/services', () => ({
  transactionsApi: {
    completeResponse: vi.fn(),
    uploadAttachment: vi.fn(),
  },
}));

describe('CompleteResponseFormPanel', () => {
  const onDirtyChange = vi.fn();
  const onSuccess = vi.fn();
  const onCancel = vi.fn();

  function renderPanel(responseType = 'Internal') {
    return render(
      <CompleteResponseFormPanel
        transactionId={1}
        responseType={responseType}
        onDirtyChange={onDirtyChange}
        onSuccess={onSuccess}
        onCancel={onCancel}
      />,
    );
  }

  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(services.transactionsApi.completeResponse).mockResolvedValue({ data: {} } as never);
    vi.mocked(services.transactionsApi.uploadAttachment).mockResolvedValue({ data: {} } as never);
  });

  afterEach(() => {
    cleanup();
  });

  async function fillRequiredFields() {
    renderPanel();
    const user = userEvent.setup();
    await user.type(screen.getByLabelText('ملخص الإفادة *'), 'ملخص الإفادة');
    return user;
  }

  it('succeeds without attachment', async () => {
    const user = await fillRequiredFields();
    await user.click(screen.getByRole('button', { name: 'تسجيل الإفادة' }));

    await waitFor(() => {
      expect(services.transactionsApi.completeResponse).toHaveBeenCalledTimes(1);
      expect(services.transactionsApi.uploadAttachment).not.toHaveBeenCalled();
      expect(onSuccess).toHaveBeenCalledWith();
    });
  });

  it('succeeds with attachment', async () => {
    const user = await fillRequiredFields();
    const file = new File(['pdf'], 'response.pdf', { type: 'application/pdf' });
    fireEvent.change(screen.getByLabelText('مرفق (اختياري)'), { target: { files: [file] } });
    await user.click(screen.getByRole('button', { name: 'تسجيل الإفادة' }));

    await waitFor(() => {
      expect(services.transactionsApi.completeResponse).toHaveBeenCalledTimes(1);
      expect(services.transactionsApi.uploadAttachment).toHaveBeenCalledTimes(1);
      expect(onSuccess).toHaveBeenCalledWith();
    });
  });

  it('reports partial success when attachment upload fails', async () => {
    vi.mocked(services.transactionsApi.uploadAttachment).mockRejectedValue(new Error('upload fail'));
    const user = await fillRequiredFields();
    const file = new File(['pdf'], 'response.pdf', { type: 'application/pdf' });
    fireEvent.change(screen.getByLabelText('مرفق (اختياري)'), { target: { files: [file] } });
    await user.click(screen.getByRole('button', { name: 'تسجيل الإفادة' }));

    await waitFor(() => {
      expect(services.transactionsApi.completeResponse).toHaveBeenCalledTimes(1);
      expect(onSuccess).toHaveBeenCalledWith({
        attachmentWarning: 'تم تسجيل الإفادة، لكن تعذر رفع المرفق. يمكنك رفعه من قسم المرفقات.',
      });
    });
  });

  it('does not call completeResponse again after attachment failure', async () => {
    vi.mocked(services.transactionsApi.uploadAttachment).mockRejectedValue(new Error('upload fail'));
    const user = await fillRequiredFields();
    const file = new File(['pdf'], 'response.pdf', { type: 'application/pdf' });
    fireEvent.change(screen.getByLabelText('مرفق (اختياري)'), { target: { files: [file] } });
    await user.click(screen.getByRole('button', { name: 'تسجيل الإفادة' }));

    await waitFor(() => expect(onSuccess).toHaveBeenCalledTimes(1));
    expect(services.transactionsApi.completeResponse).toHaveBeenCalledTimes(1);

    const retryFile = new File(['pdf2'], 'retry.pdf', { type: 'application/pdf' });
    fireEvent.change(screen.getByLabelText('مرفق (اختياري)'), { target: { files: [retryFile] } });
    await user.click(screen.getByRole('button', { name: 'تسجيل الإفادة' }));

    await waitFor(() => expect(services.transactionsApi.uploadAttachment).toHaveBeenCalledTimes(2));
    expect(services.transactionsApi.completeResponse).toHaveBeenCalledTimes(1);
  });

  it('clears dirty state after response is saved', async () => {
    const user = await fillRequiredFields();
    onDirtyChange.mockClear();
    await user.click(screen.getByRole('button', { name: 'تسجيل الإفادة' }));

    await waitFor(() => {
      expect(onDirtyChange).toHaveBeenCalledWith(false);
    });
  });
});
