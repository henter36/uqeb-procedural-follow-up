import type { ScanRequest, ScanResult, ScannerBridgeStatus, ScannerDevice } from './scannerTypes';

const MOCK_SCANNERS: ScannerDevice[] = [
  { id: 'mock:scanner-1', name: 'ماسح تجريبي (Mock)', default: true },
];

let mockScanCounter = 0;

function drawMockDocument(canvas: HTMLCanvasElement): void {
  const ctx = canvas.getContext('2d');
  if (!ctx) return;

  ctx.fillStyle = '#ffffff';
  ctx.fillRect(0, 0, canvas.width, canvas.height);
  ctx.strokeStyle = '#d1d5db';
  ctx.lineWidth = 2;
  ctx.strokeRect(24, 24, canvas.width - 48, canvas.height - 48);

  ctx.fillStyle = '#1f2937';
  ctx.font = 'bold 28px Arial';
  ctx.textAlign = 'right';
  ctx.fillText('وثيقة مسح ضوئي تجريبية', canvas.width - 40, 80);

  ctx.font = '18px Arial';
  ctx.fillStyle = '#4b5563';
  ctx.fillText('Uqeb — Mock Scanner Bridge', canvas.width - 40, 120);
  ctx.fillText('هذه معاينة وهمية لأغراض التطوير فقط.', canvas.width - 40, 160);
  ctx.fillText(new Date().toLocaleString('ar-SA'), canvas.width - 40, 200);
}

export async function canvasToJpegBlob(canvas: HTMLCanvasElement, quality = 0.92): Promise<Blob> {
  return new Promise((resolve, reject) => {
    canvas.toBlob((blob) => {
      if (blob) resolve(blob);
      else reject(new Error('Failed to create image blob'));
    }, 'image/jpeg', quality);
  });
}

export async function createMockPreviewBase64(): Promise<{ base64: string; width: number; height: number }> {
  const canvas = document.createElement('canvas');
  canvas.width = 420;
  canvas.height = 594;
  drawMockDocument(canvas);
  const dataUrl = canvas.toDataURL('image/jpeg', 0.75);
  return {
    base64: dataUrl.split(',')[1] ?? '',
    width: canvas.width,
    height: canvas.height,
  };
}

export async function mockGetStatus(): Promise<ScannerBridgeStatus> {
  return {
    ok: true,
    version: 'mock-0.1.0',
    scannerApi: 'Mock',
    scannerCount: MOCK_SCANNERS.length,
  };
}

export async function mockGetScanners(): Promise<ScannerDevice[]> {
  return [...MOCK_SCANNERS];
}

export async function mockScan(request: ScanRequest): Promise<ScanResult> {
  void request;
  mockScanCounter += 1;
  const preview = await createMockPreviewBase64();
  const scanId = `mock-scan-${mockScanCounter}-${Date.now()}`;

  return {
    scanId,
    contentType: 'image/jpeg',
    fileName: `scan-mock-${mockScanCounter}.jpg`,
    width: preview.width,
    height: preview.height,
    previewBase64: preview.base64,
    expiresAtUtc: new Date(Date.now() + 10 * 60 * 1000).toISOString(),
  };
}

export async function mockGetScanFile(scanId: string): Promise<Blob> {
  if (!scanId.startsWith('mock-scan-')) {
    throw new Error('Unknown mock scan id');
  }

  const canvas = document.createElement('canvas');
  canvas.width = 1240;
  canvas.height = 1754;
  drawMockDocument(canvas);
  return canvasToJpegBlob(canvas);
}
