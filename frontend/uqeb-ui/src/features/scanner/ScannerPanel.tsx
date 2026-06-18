import { useEffect, useState } from 'react';
import { transactionsApi } from '../../api/services';
import { getScannerErrorMessage, ScannerBridgeError } from './scannerErrors';
import { deleteScan, isScannerMockMode } from './scannerBridgeClient';
import { useScannerBridge } from './useScannerBridge';

interface ScannerPanelProps {
  transactionId: number;
  onClose: () => void;
  onSaved: () => void;
}

export default function ScannerPanel({ transactionId, onClose, onSaved }: ScannerPanelProps) {
  const {
    phase,
    scanners,
    selectedScannerId,
    scanResult,
    previewUrl,
    errorMessage,
    isMock,
    setSelectedScannerId,
    initialize,
    runScan,
    rotatePreview,
    resetPreview,
    getFileForUpload,
  } = useScannerBridge();

  const [saveError, setSaveError] = useState('');

  useEffect(() => {
    initialize();
  }, [initialize]);

  const handleClose = async () => {
    if (scanResult && !isScannerMockMode()) {
      await deleteScan(scanResult.scanId);
    }
    onClose();
  };

  const handleSave = async () => {
    setSaveError('');
    try {
      const file = await getFileForUpload();
      await transactionsApi.uploadAttachment(transactionId, file, 'Scan');
      if (scanResult && !isScannerMockMode()) {
        await deleteScan(scanResult.scanId);
      }
      onSaved();
      onClose();
    } catch (err) {
      const message = err instanceof ScannerBridgeError
        ? err.message
        : getScannerErrorMessage('UPLOAD_FAILED');
      setSaveError(message);
    }
  };

  const showMockBadge = isMock || isScannerMockMode();

  return (
    <div className="modal-overlay" onClick={handleClose}>
      <div
        className="modal scanner-panel-modal"
        onClick={(e) => e.stopPropagation()}
        role="dialog"
        aria-labelledby="scanner-panel-title"
      >
        <h3 id="scanner-panel-title">مسح ضوئي</h3>

        {showMockBadge && (
          <div className="alert alert-info scanner-mock-badge">
            وضع تجريبي (Mock) — لا يتصل بماسح فعلي.
          </div>
        )}

        {phase === 'checking' && <div className="loading">جاري التحقق من خدمة الماسح...</div>}

        {phase === 'offline' && (
          <div className="alert alert-error">{errorMessage || 'خدمة الماسح غير متاحة.'}</div>
        )}

        {phase === 'no-scanner' && (
          <div className="alert alert-error">{errorMessage}</div>
        )}

        {(phase === 'ready' || phase === 'error') && (
          <>
            {errorMessage && phase === 'error' && (
              <div className="alert alert-error">{errorMessage}</div>
            )}
            <div className="form-group">
              <label htmlFor="scanner-select">الماسح</label>
              <select
                id="scanner-select"
                value={selectedScannerId}
                onChange={(e) => setSelectedScannerId(e.target.value)}
              >
                {scanners.map((scanner) => (
                  <option key={scanner.id} value={scanner.id}>{scanner.name}</option>
                ))}
              </select>
            </div>
            <div className="scanner-actions">
              <button type="button" className="btn btn-primary" onClick={runScan}>
                بدء المسح
              </button>
              <button type="button" className="btn btn-secondary" onClick={handleClose}>
                إلغاء
              </button>
            </div>
          </>
        )}

        {phase === 'scanning' && <div className="loading">جاري المسح...</div>}

        {phase === 'preview' && previewUrl && (
          <>
            <div className="scanner-preview-wrap">
              <img src={previewUrl} alt="معاينة المسح الضوئي" className="scanner-preview" />
            </div>
            {saveError && <div className="alert alert-error">{saveError}</div>}
            <div className="scanner-actions">
              <button type="button" className="btn btn-primary" onClick={handleSave}>
                حفظ كمرفق
              </button>
              <button type="button" className="btn btn-secondary" onClick={rotatePreview}>
                تدوير
              </button>
              <button type="button" className="btn btn-secondary" onClick={runScan}>
                إعادة المسح
              </button>
              <button type="button" className="btn btn-secondary" onClick={resetPreview}>
                إلغاء المعاينة
              </button>
              <button type="button" className="btn btn-secondary" onClick={handleClose}>
                إلغاء
              </button>
            </div>
          </>
        )}

        {(phase === 'offline' || phase === 'no-scanner') && (
          <div className="scanner-actions">
            <button type="button" className="btn btn-secondary" onClick={handleClose}>
              إغلاق
            </button>
          </div>
        )}
      </div>
    </div>
  );
}
