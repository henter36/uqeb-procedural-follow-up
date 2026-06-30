import { useCallback, useState } from 'react';
import type { ScanResult, ScannerDevice, ScannerPanelPhase } from './scannerTypes';
import {
  getBridgeStatus,
  getScanFile,
  getScanners,
  isScannerConfigured,
  isScannerMockMode,
  scanDocument,
} from './scannerBridgeClient';
import { ScannerBridgeError } from './scannerErrors';

interface UseScannerBridgeState {
  phase: ScannerPanelPhase;
  scanners: ScannerDevice[];
  selectedScannerId: string;
  scanResult: ScanResult | null;
  previewUrl: string | null;
  errorMessage: string;
  isMock: boolean;
  setSelectedScannerId: (id: string) => void;
  initialize: () => Promise<void>;
  runScan: () => Promise<void>;
  rotatePreview: () => void;
  resetPreview: () => void;
  getFileForUpload: () => Promise<File>;
}

function buildPreviewUrl(previewBase64: string, contentType: string): string {
  return `data:${contentType};base64,${previewBase64}`;
}

async function rotateImageDataUrl(dataUrl: string): Promise<string> {
  const image = await new Promise<HTMLImageElement>((resolve, reject) => {
    const img = new Image();
    img.onload = () => resolve(img);
    img.onerror = () => reject(new Error('Failed to load preview'));
    img.src = dataUrl;
  });

  const canvas = document.createElement('canvas');
  canvas.width = image.height;
  canvas.height = image.width;
  const ctx = canvas.getContext('2d');
  if (!ctx) throw new Error('Canvas not supported');

  ctx.translate(canvas.width / 2, canvas.height / 2);
  ctx.rotate(Math.PI / 2);
  ctx.drawImage(image, -image.width / 2, -image.height / 2);
  return canvas.toDataURL('image/jpeg', 0.92);
}

export function useScannerBridge(): UseScannerBridgeState {
  const [phase, setPhase] = useState<ScannerPanelPhase>('checking');
  const [scanners, setScanners] = useState<ScannerDevice[]>([]);
  const [selectedScannerId, setSelectedScannerId] = useState('');
  const [scanResult, setScanResult] = useState<ScanResult | null>(null);
  const [previewUrl, setPreviewUrl] = useState<string | null>(null);
  const [isRotated, setIsRotated] = useState(false);
  const [errorMessage, setErrorMessage] = useState('');
  const isMock = isScannerMockMode();

  const initialize = useCallback(async () => {
    setPhase('checking');
    setErrorMessage('');
    setScanResult(null);
    setPreviewUrl(null);
    setIsRotated(false);

    if (!isScannerMockMode() && !isScannerConfigured()) {
      setPhase('offline');
      setErrorMessage('خدمة الماسح غير مهيأة.');
      return;
    }

    try {
      const status = await getBridgeStatus();
      if (!status.ok) {
        setPhase('offline');
        setErrorMessage('خدمة الماسح غير متاحة.');
        return;
      }

      const devices = await getScanners();
      if (devices.length === 0) {
        setPhase('no-scanner');
        setErrorMessage('لا يوجد ماسح متصل بهذا الجهاز.');
        return;
      }

      setScanners(devices);
      const defaultDevice = devices.find((d) => d.default) ?? devices[0];
      setSelectedScannerId(defaultDevice.id);
      setPhase('ready');
    } catch (err) {
      const message = err instanceof ScannerBridgeError
        ? err.message
        : 'خدمة الماسح غير متاحة.';
      setErrorMessage(message);
      setPhase('offline');
    }
  }, []);

  const runScan = useCallback(async () => {
    if (!selectedScannerId) {
      setErrorMessage('اختر ماسحًا أولًا.');
      setPhase('error');
      return;
    }

    setPhase('scanning');
    setErrorMessage('');
    setIsRotated(false);

    try {
      const result = await scanDocument({
        scannerId: selectedScannerId,
        format: 'image/jpeg',
        dpi: 300,
        colorMode: 'color',
      });
      setScanResult(result);
      setPreviewUrl(buildPreviewUrl(result.previewBase64, result.contentType));
      setIsRotated(false);
      setPhase('preview');
    } catch (err) {
      const message = err instanceof ScannerBridgeError
        ? err.message
        : 'فشل الاتصال بالماسح.';
      setErrorMessage(message);
      setPhase('error');
    }
  }, [selectedScannerId]);

  const rotatePreview = useCallback(() => {
    if (!previewUrl) return;
    rotateImageDataUrl(previewUrl)
      .then((rotated) => {
        setPreviewUrl(rotated);
        setIsRotated(true);
      })
      .catch(() => setErrorMessage('تعذر تدوير المعاينة.'));
  }, [previewUrl]);

  const resetPreview = useCallback(() => {
    setScanResult(null);
    setPreviewUrl(null);
    setIsRotated(false);
    setErrorMessage('');
    setPhase(scanners.length > 0 ? 'ready' : 'no-scanner');
  }, [scanners.length]);

  const getFileForUpload = useCallback(async (): Promise<File> => {
    if (!scanResult) {
      throw new ScannerBridgeError('SCAN_FAILED');
    }

    if (isRotated && previewUrl?.startsWith('data:')) {
      const response = await fetch(previewUrl);
      const blob = await response.blob();
      return new File([blob], scanResult.fileName, { type: blob.type || scanResult.contentType });
    }

    const blob = await getScanFile(scanResult.scanId);
    return new File([blob], scanResult.fileName, { type: scanResult.contentType });
  }, [isRotated, previewUrl, scanResult]);

  return {
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
  };
}
