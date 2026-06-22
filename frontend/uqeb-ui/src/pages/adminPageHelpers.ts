export function normalizeName(value: string) {
  return value.trim().replace(/\s+/g, ' ').toLowerCase();
}

export function normalizeTrimmedName(value: string) {
  return value.trim().replace(/\s+/g, ' ');
}

export function isEmptyTrimmedName(value: string) {
  return normalizeTrimmedName(value).length === 0;
}
