export type ScannerErrorCode =
  | 'BRIDGE_OFFLINE'
  | 'NO_SCANNER'
  | 'SCAN_FAILED'
  | 'UPLOAD_FAILED'
  | 'BRIDGE_TIMEOUT';

const messages: Record<ScannerErrorCode, string> = {
  BRIDGE_OFFLINE: 'خدمة الماسح غير متاحة. يمكنك رفع ملف يدويًا.',
  NO_SCANNER: 'لا يوجد ماسح متصل بهذا الجهاز.',
  SCAN_FAILED: 'فشل الاتصال بالماسح. تحقق من تشغيل الجهاز وأعد المحاولة.',
  UPLOAD_FAILED: 'فشل حفظ المرفق. الملف لم يُحفظ في المعاملة.',
  BRIDGE_TIMEOUT: 'انتهت مهلة الاتصال بخدمة الماسح.',
};

export function getScannerErrorMessage(code: ScannerErrorCode): string {
  return messages[code];
}

export class ScannerBridgeError extends Error {
  readonly code: ScannerErrorCode;

  constructor(code: ScannerErrorCode, message?: string) {
    super(message ?? getScannerErrorMessage(code));
    this.code = code;
    this.name = 'ScannerBridgeError';
  }
}
