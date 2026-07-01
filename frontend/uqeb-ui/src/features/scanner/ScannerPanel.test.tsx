import { act, cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import ScannerPanel from './ScannerPanel';
import { ScannerBridgeError } from './scannerErrors';
import * as scannerBridgeClient from './scannerBridgeClient';

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
});
