const UUID_V4_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;

function createUuidFromRandomValues(): string | null {
  if (typeof crypto === 'undefined' || typeof crypto.getRandomValues !== 'function') {
    return null;
  }

  const bytes = new Uint8Array(16);
  crypto.getRandomValues(bytes);
  bytes[6] = (bytes[6] & 0x0f) | 0x40;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;

  const hex = Array.from(bytes, (byte) => byte.toString(16).padStart(2, '0')).join('');
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}

export function createIdempotencyKey(): string {
  if (typeof crypto !== 'undefined' && typeof crypto.randomUUID === 'function') {
    return crypto.randomUUID();
  }

  const uuid = createUuidFromRandomValues();
  if (uuid && UUID_V4_PATTERN.test(uuid)) {
    return uuid;
  }

  throw new Error(
    'Web Crypto API غير متوفر في هذه البيئة. لا يمكن إنشاء مفتاح idempotency آمن.',
  );
}
