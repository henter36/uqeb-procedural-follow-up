import { act, cleanup, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import userEvent from '@testing-library/user-event';
import ScannerPanel from './ScannerPanel';
import { ScannerBridgeError } from './scannerErrors';
import * as scannerBridgeClient from './scannerBridgeClient';
import { transactionsApi } from '../../api/services';

vi.mock('./scannerBridgeClient', () => ({
  isScannerMockMode: vi.fn().mockReturnValue(false),
  getBridgeStatus: vi.fn(),
  getScanners: vi.fn(),
  scanDocument: vi.fn(),
  getScanFile: vi.fn(),
  deleteScan: vi.fn(),
  getScannerBridgeBaseUrl: vi.fn().mockReturnValue('http://127.0.0.1:5055'),
}));

vi.mock('../../api/services', () => ({
  transactionsApi: {
    uploadAttachment: vi.fn(),
  },
}));

describe('ScannerPanel', () => {
  beforeEach(() => {
    vi.mocked(scannerBridgeClient.deleteScan).mockResolvedValue(undefined);
  });

  afterEach(() => {
    cleanup();
    vi.clearAllMocks();
  });

  it('shows local bridge offline message when the scanner service is not running', async () => {
    vi.mocked(scannerBridgeClient.getBridgeStatus).mockRejectedValue(new ScannerBridgeError('BRIDGE_OFFLINE'));

    await act(async () => {
      render(<ScannerPanel transactionId={1} onClose={vi.fn()} onSaved={vi.fn()} />);
    });
    expect(screen.getByText('خدمة الماسح المحلية غير شغالة على هذا الجهاز. شغّل Uqeb Scanner Bridge أو استخدم إرفاق ملف يدويًا.')).toBeTruthy();
  });

  it('uploads scanned files as transaction attachments by default', async () => {
    const user = userEvent.setup();
    const onClose = vi.fn();
    const onSaved = vi.fn();
    vi.mocked(scannerBridgeClient.getBridgeStatus).mockResolvedValue({
      ok: true,
      version: '1.0.0',
      scannerApi: 'mock',
      scannerCount: 1,
    });
    vi.mocked(scannerBridgeClient.getScanners).mockResolvedValue([{ id: 'scanner-1', name: 'Scanner', default: true }]);
    vi.mocked(scannerBridgeClient.scanDocument).mockResolvedValue({
      scanId: 'scan-1',
      fileName: 'scan.jpg',
      contentType: 'image/jpeg',
      width: 100,
      height: 100,
      previewBase64: 'ZmFrZQ==',
      expiresAtUtc: '2026-01-01T00:00:00Z',
    });
    vi.mocked(scannerBridgeClient.getScanFile).mockResolvedValue(new Blob(['scan'], { type: 'image/jpeg' }));
    vi.mocked(transactionsApi.uploadAttachment).mockResolvedValue({ data: {} } as never);

    render(<ScannerPanel transactionId={1} onClose={onClose} onSaved={onSaved} />);

    await waitFor(() => expect(screen.getByRole('button', { name: 'بدء المسح' })).toBeInTheDocument());
    await user.click(screen.getByRole('button', { name: 'بدء المسح' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'حفظ كمرفق' })).toBeInTheDocument());
    await user.click(screen.getByRole('button', { name: 'حفظ كمرفق' }));

    await waitFor(() => {
      expect(transactionsApi.uploadAttachment).toHaveBeenCalledWith(1, expect.any(File), 'Scan');
      expect(onSaved).toHaveBeenCalled();
      expect(onClose).toHaveBeenCalled();
    });
  });

  it('uses custom scanned-file saver without uploading a transaction attachment', async () => {
    const user = userEvent.setup();
    const onClose = vi.fn();
    const onSaved = vi.fn();
    const onSaveScannedFile = vi.fn().mockResolvedValue(undefined);
    vi.mocked(scannerBridgeClient.getBridgeStatus).mockResolvedValue({
      ok: true,
      version: '1.0.0',
      scannerApi: 'mock',
      scannerCount: 1,
    });
    vi.mocked(scannerBridgeClient.getScanners).mockResolvedValue([{ id: 'scanner-1', name: 'Scanner', default: true }]);
    vi.mocked(scannerBridgeClient.scanDocument).mockResolvedValue({
      scanId: 'scan-1',
      fileName: 'scan.jpg',
      contentType: 'image/jpeg',
      width: 100,
      height: 100,
      previewBase64: 'ZmFrZQ==',
      expiresAtUtc: '2026-01-01T00:00:00Z',
    });
    vi.mocked(scannerBridgeClient.getScanFile).mockResolvedValue(new Blob(['scan'], { type: 'image/jpeg' }));

    render(
      <ScannerPanel
        transactionId={1}
        onClose={onClose}
        onSaved={onSaved}
        onSaveScannedFile={onSaveScannedFile}
      />,
    );

    await waitFor(() => expect(screen.getByRole('button', { name: 'بدء المسح' })).toBeInTheDocument());
    await user.click(screen.getByRole('button', { name: 'بدء المسح' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'حفظ كمرفق' })).toBeInTheDocument());
    await user.click(screen.getByRole('button', { name: 'حفظ كمرفق' }));

    await waitFor(() => {
      expect(onSaveScannedFile).toHaveBeenCalledWith(expect.any(File));
      expect(transactionsApi.uploadAttachment).not.toHaveBeenCalled();
      expect(onSaved).toHaveBeenCalled();
      expect(onClose).toHaveBeenCalled();
    });
  });
});
