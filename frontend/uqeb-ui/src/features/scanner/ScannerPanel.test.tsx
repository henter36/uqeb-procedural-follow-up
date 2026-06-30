import { act, cleanup, render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import ScannerPanel from './ScannerPanel';

vi.mock('./scannerBridgeClient', () => ({
  isScannerMockMode: vi.fn().mockReturnValue(false),
  isScannerConfigured: vi.fn().mockReturnValue(false),
  getBridgeStatus: vi.fn(),
  getScanners: vi.fn(),
  scanDocument: vi.fn(),
  getScanFile: vi.fn(),
  deleteScan: vi.fn(),
  getScannerBridgeBaseUrl: vi.fn().mockReturnValue(''),
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

  it('shows configuration message when VITE_SCANNER_BRIDGE_URL is not set', async () => {
    await act(async () => {
      render(<ScannerPanel transactionId={1} onClose={vi.fn()} onSaved={vi.fn()} />);
    });
    expect(screen.getByText('خدمة الماسح غير مهيأة.')).toBeTruthy();
  });
});
