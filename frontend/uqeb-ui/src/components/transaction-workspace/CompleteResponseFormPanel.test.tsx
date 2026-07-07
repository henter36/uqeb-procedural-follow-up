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

vi.mock('../../features/scanner/ScannerPanel', () => ({
  default: ({
    onSaveScannedFile,
    onSaved,
    onClose,
  }: {
    onSaveScannedFile?: (file: File) => Promise<void>;
    onSaved: () => void;
    onClose: () => void;
  }) => (
    <div>
      <button
        type="button"
        onClick={async () => {
          await onSaveScannedFile?.(new File(['scan'], 'scan.jpg', { type: 'image/jpeg' }));
          onSaved();
          onClose();
        }}
      >
        حفظ كمرفق
      </button>
    </div>
  ),
}));

describe('CompleteResponseFormPanel', () => {
  const onDirtyChange = vi.fn();
  const onSuccess = vi.fn();
  const onCancel = vi.fn();

  function renderPanel(responseType = 'Internal', suggestedResponseDate?: string) {
    return render(
      <CompleteResponseFormPanel
        transactionId={1}
        responseType={responseType}
        suggestedResponseDate={suggestedResponseDate}
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
    await user.type(screen.getByLabelText('تاريخ الإفادة *'), '16/01/1448');
    await user.type(screen.getByLabelText('ملخص الإفادة *'), 'ملخص الإفادة');
    return user;
  }

  it('prefills the response date with the suggested procedural completion date', () => {
    renderPanel('Internal', '2026-01-09T00:00:00');
    expect(screen.getByLabelText('تاريخ الإفادة *')).toHaveValue('20/07/1447');
  });

  it('lets the user override the suggested response date before submitting', async () => {
    renderPanel('Internal', '2026-01-09T00:00:00');
    const user = userEvent.setup();
    const responseDate = screen.getByLabelText('تاريخ الإفادة *');
    await user.clear(responseDate);
    await user.type(responseDate, '24/07/1447');
    await user.type(screen.getByLabelText('ملخص الإفادة *'), 'ملخص الإفادة');
    await user.click(screen.getByRole('button', { name: 'إرسال الإفادة' }));

    await waitFor(() => {
      expect(services.transactionsApi.completeResponse).toHaveBeenCalledWith(1, expect.objectContaining({
        responseDate: '2026-01-13T00:00:00',
      }));
    });
  });

  it('does not prefill the response date when there is no suggested completion date', () => {
    renderPanel();
    expect(screen.getByLabelText('تاريخ الإفادة *')).toHaveValue('');
  });

  it('renders as compact editor with small textarea', () => {
    renderPanel();
    const textarea = screen.getByLabelText('ملخص الإفادة *');
    expect(textarea).toHaveAttribute('rows', '4');
    expect(screen.getByRole('button', { name: 'إرسال الإفادة' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'إلغاء' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'رفع ملف' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'مسح ضوئي' })).toBeInTheDocument();
  });

  it('opens with an empty response date and does not default to today', () => {
    renderPanel();
    expect(screen.getByLabelText('تاريخ الإفادة *')).toHaveValue('');
  });

  it('opens with an empty outgoing date for response types that require outgoing details', () => {
    renderPanel('External');
    expect(screen.getByLabelText('تاريخ الإفادة *')).toHaveValue('');
    expect(screen.getByLabelText('تاريخ الصادر *')).toHaveValue('');
  });

  it('does not submit without a response date', async () => {
    renderPanel();
    const user = userEvent.setup();
    await user.type(screen.getByLabelText('ملخص الإفادة *'), 'ملخص الإفادة');
    await user.click(screen.getByRole('button', { name: 'إرسال الإفادة' }));

    expect(await screen.findByText('تاريخ الإفادة مطلوب.')).toBeInTheDocument();
    expect(services.transactionsApi.completeResponse).not.toHaveBeenCalled();
  });

  it('does not submit a response requiring outgoing details without an outgoing date', async () => {
    renderPanel('External');
    const user = userEvent.setup();
    await user.type(screen.getByLabelText('تاريخ الإفادة *'), '16/01/1448');
    await user.type(screen.getByLabelText('ملخص الإفادة *'), 'ملخص الإفادة');
    await user.type(screen.getByLabelText('رقم الصادر *'), 'OUT-1');
    await user.click(screen.getByRole('button', { name: 'إرسال الإفادة' }));

    expect(await screen.findByText('تاريخ الصادر مطلوب.')).toBeInTheDocument();
    expect(services.transactionsApi.completeResponse).not.toHaveBeenCalled();
  });

  it('does not submit a response requiring outgoing details without an outgoing number', async () => {
    renderPanel('External');
    const user = userEvent.setup();
    await user.type(screen.getByLabelText('تاريخ الإفادة *'), '16/01/1448');
    await user.type(screen.getByLabelText('ملخص الإفادة *'), 'ملخص الإفادة');
    await user.type(screen.getByLabelText('تاريخ الصادر *'), '16/01/1448');
    await user.click(screen.getByRole('button', { name: 'إرسال الإفادة' }));

    expect(await screen.findByText('رقم الصادر مطلوب.')).toBeInTheDocument();
    expect(services.transactionsApi.completeResponse).not.toHaveBeenCalled();
  });

  it('converts a non-today Hijri outgoing date to Gregorian ISO before submit', async () => {
    renderPanel('External');
    const user = userEvent.setup();
    await user.type(screen.getByLabelText('تاريخ الإفادة *'), '16/01/1448');
    await user.type(screen.getByLabelText('ملخص الإفادة *'), 'ملخص الإفادة');
    await user.type(screen.getByLabelText('رقم الصادر *'), 'OUT-1');
    await user.type(screen.getByLabelText('تاريخ الصادر *'), '16/01/1448');
    await user.click(screen.getByRole('button', { name: 'إرسال الإفادة' }));

    await waitFor(() => {
      expect(services.transactionsApi.completeResponse).toHaveBeenCalledWith(1, expect.objectContaining({
        outgoingDate: '2026-07-01T00:00:00',
      }));
    });
  });

  it('attachment toolbar uses fieldset not role=group', () => {
    const { container } = renderPanel();
    expect(container.querySelector('fieldset.complete-response-attachment-toolbar')).toBeInTheDocument();
    expect(container.querySelector('[role="group"]')).not.toBeInTheDocument();
  });

  it('scan button sets scanned file as attachment without uploading transaction attachment', async () => {
    const user = userEvent.setup();
    renderPanel();
    await user.click(screen.getByRole('button', { name: 'مسح ضوئي' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'حفظ كمرفق' })).toBeInTheDocument());
    await user.click(screen.getByRole('button', { name: 'حفظ كمرفق' }));
    expect(services.transactionsApi.uploadAttachment).not.toHaveBeenCalled();
    expect(screen.queryByRole('button', { name: 'حفظ كمرفق' })).not.toBeInTheDocument();
  });

  it('succeeds without attachment', async () => {
    const user = await fillRequiredFields();
    await user.click(screen.getByRole('button', { name: 'إرسال الإفادة' }));

    await waitFor(() => {
      expect(services.transactionsApi.completeResponse).toHaveBeenCalledTimes(1);
      expect(services.transactionsApi.uploadAttachment).not.toHaveBeenCalled();
      expect(onSuccess).toHaveBeenCalledWith();
    });
  });

  it('converts Hijri response date to Gregorian ISO before submit', async () => {
    const user = await fillRequiredFields();
    const responseDate = screen.getByLabelText('تاريخ الإفادة *');
    await user.clear(responseDate);
    await user.type(responseDate, '16/01/1448');
    await user.click(screen.getByRole('button', { name: 'إرسال الإفادة' }));

    await waitFor(() => {
      expect(services.transactionsApi.completeResponse).toHaveBeenCalledWith(1, expect.objectContaining({
        responseDate: '2026-07-01T00:00:00',
      }));
    });
  });

  it('succeeds with attachment', async () => {
    const user = await fillRequiredFields();
    const file = new File(['pdf'], 'response.pdf', { type: 'application/pdf' });
    fireEvent.change(screen.getByLabelText('مرفق (اختياري)'), { target: { files: [file] } });
    await user.click(screen.getByRole('button', { name: 'إرسال الإفادة' }));

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
    await user.click(screen.getByRole('button', { name: 'إرسال الإفادة' }));

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
    await user.click(screen.getByRole('button', { name: 'إرسال الإفادة' }));

    await waitFor(() => expect(onSuccess).toHaveBeenCalledTimes(1));
    expect(services.transactionsApi.completeResponse).toHaveBeenCalledTimes(1);

    const retryFile = new File(['pdf2'], 'retry.pdf', { type: 'application/pdf' });
    fireEvent.change(screen.getByLabelText('مرفق (اختياري)'), { target: { files: [retryFile] } });
    await user.click(screen.getByRole('button', { name: 'إرسال الإفادة' }));

    await waitFor(() => expect(services.transactionsApi.uploadAttachment).toHaveBeenCalledTimes(2));
    expect(services.transactionsApi.completeResponse).toHaveBeenCalledTimes(1);
  });

  it('clears dirty state after response is saved', async () => {
    const user = await fillRequiredFields();
    onDirtyChange.mockClear();
    await user.click(screen.getByRole('button', { name: 'إرسال الإفادة' }));

    await waitFor(() => {
      expect(onDirtyChange).toHaveBeenCalledWith(false);
    });
  });
});
