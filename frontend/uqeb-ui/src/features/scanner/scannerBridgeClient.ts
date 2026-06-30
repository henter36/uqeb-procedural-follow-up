import type { ScanRequest, ScanResult, ScannerBridgeStatus, ScannerDevice } from './scannerTypes';
import { ScannerBridgeError } from './scannerErrors';
import {
  mockGetScanFile,
  mockGetScanners,
  mockGetStatus,
  mockScan,
} from './scannerMock';

const REQUEST_TIMEOUT_MS = 4000;

export function isScannerConfigured(): boolean {
  const configured = import.meta.env.VITE_SCANNER_BRIDGE_URL;
  return typeof configured === 'string' && configured.trim().length > 0;
}

export function getScannerBridgeBaseUrl(): string {
  const configured = import.meta.env.VITE_SCANNER_BRIDGE_URL;
  return typeof configured === 'string' ? configured.trim() : '';
}

export function isScannerMockMode(): boolean {
  return import.meta.env.VITE_SCANNER_MOCK === 'true';
}

async function fetchWithTimeout(url: string, init?: RequestInit): Promise<Response> {
  const controller = new AbortController();
  const timeoutId = window.setTimeout(() => controller.abort(), REQUEST_TIMEOUT_MS);

  try {
    return await fetch(url, { ...init, signal: controller.signal });
  } catch (err) {
    if (err instanceof DOMException && err.name === 'AbortError') {
      throw new ScannerBridgeError('BRIDGE_TIMEOUT');
    }
    throw new ScannerBridgeError('BRIDGE_OFFLINE');
  } finally {
    window.clearTimeout(timeoutId);
  }
}

async function readJson<T>(response: Response): Promise<T> {
  if (!response.ok) {
    throw new ScannerBridgeError('SCAN_FAILED');
  }
  return response.json() as Promise<T>;
}

export async function getBridgeStatus(): Promise<ScannerBridgeStatus> {
  if (isScannerMockMode()) {
    return mockGetStatus();
  }

  const response = await fetchWithTimeout(`${getScannerBridgeBaseUrl()}/status`);
  return readJson<ScannerBridgeStatus>(response);
}

export async function getScanners(): Promise<ScannerDevice[]> {
  if (isScannerMockMode()) {
    return mockGetScanners();
  }

  const response = await fetchWithTimeout(`${getScannerBridgeBaseUrl()}/scanners`);
  const data = await readJson<{ scanners: ScannerDevice[] }>(response);
  return data.scanners ?? [];
}

export async function scanDocument(request: ScanRequest): Promise<ScanResult> {
  if (isScannerMockMode()) {
    return mockScan(request);
  }

  const response = await fetchWithTimeout(`${getScannerBridgeBaseUrl()}/scan`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(request),
  });
  return readJson<ScanResult>(response);
}

export async function getScanFile(scanId: string): Promise<Blob> {
  if (isScannerMockMode()) {
    return mockGetScanFile(scanId);
  }

  const response = await fetchWithTimeout(`${getScannerBridgeBaseUrl()}/scan/${encodeURIComponent(scanId)}/file`);
  if (!response.ok) {
    throw new ScannerBridgeError('SCAN_FAILED');
  }
  return response.blob();
}

export async function deleteScan(scanId: string): Promise<void> {
  if (isScannerMockMode()) {
    return;
  }

  try {
    await fetchWithTimeout(`${getScannerBridgeBaseUrl()}/scan/${encodeURIComponent(scanId)}`, {
      method: 'DELETE',
    });
  } catch {
    // Best-effort cleanup; ignore bridge offline on cancel.
  }
}
