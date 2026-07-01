export type ScannerErrorCode =
  | 'BRIDGE_OFFLINE'
  | 'NO_SCANNER'
  | 'SCAN_FAILED'
  | 'UPLOAD_FAILED'
  | 'BRIDGE_TIMEOUT';

const messages: Record<ScannerErrorCode, string> = {
  BRIDGE_OFFLINE: 'خدمة الماسح المحلية غير شغالة على هذا الجهاز. شغّل Uqeb Scanner Bridge أو استخدم إرفاق ملف يدويًا.',
  NO_SCANNER: 'لم يتم العثور على ماسح عبر WIA. تأكد من تعريف الماسح في Windows أو استخدم برنامج الشركة ثم ارفع الملف يدويًا.',
  SCAN_FAILED: 'فشل الاتصال بالماسح. تحقق من تشغيل الجهاز وأعد المحاولة.',
  UPLOAD_FAILED: 'فشل حفظ المرفق. الملف لم يُحفظ في المعاملة.',
  BRIDGE_TIMEOUT: 'انتهت مهلة الاتصال بخدمة الماسح المحلية. شغّل Uqeb Scanner Bridge أو استخدم إرفاق ملف يدويًا.',
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
