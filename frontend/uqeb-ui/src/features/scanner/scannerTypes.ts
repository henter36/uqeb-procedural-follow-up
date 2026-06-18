export interface ScannerBridgeStatus {
  ok: boolean;
  version: string;
  scannerApi: string;
  scannerCount: number;
}

export interface ScannerDevice {
  id: string;
  name: string;
  default?: boolean;
}

export interface ScanRequest {
  scannerId: string;
  format?: 'image/jpeg' | 'image/png';
  dpi?: number;
  colorMode?: 'color' | 'grayscale';
}

export interface ScanResult {
  scanId: string;
  contentType: string;
  fileName: string;
  width: number;
  height: number;
  previewBase64: string;
  expiresAtUtc: string;
}

export type ScannerPanelPhase =
  | 'checking'
  | 'offline'
  | 'no-scanner'
  | 'ready'
  | 'scanning'
  | 'preview'
  | 'saving'
  | 'error';
